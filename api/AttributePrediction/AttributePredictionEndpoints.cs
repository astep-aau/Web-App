using Npgsql;

/// <summary>
/// Endpoints for the Attribute Prediction demo, all under /api/attribute-prediction.
/// The chosen partition (train / val / test) of a run's split is the set of segments we look at:
/// we compare the model's predictions there with the real road type.
/// </summary>
public static class AttributePredictionEndpoints
{
    static readonly string[] Partitions = ["train", "val", "test"];

    public static void Map(WebApplication app)
    {
        var dataSource = NpgsqlDataSource.Create(Database.ConnectionString(app.Configuration));
        var api = app.MapGroup("/api/attribute-prediction");

        api.MapGet("/runs", async () =>
        {
            await using var command = dataSource.CreateCommand(
                """
                SELECT r.run_id, a.name, r.architecture, s.method, f.name, f.version, r.status::text, r.started_at
                FROM attribute_prediction.model_run r
                JOIN attribute_prediction.attribute a USING (attribute_id)
                JOIN attribute_prediction.split s USING (split_id)
                JOIN attribute_prediction.feature_set f USING (feature_set_id)
                WHERE EXISTS (SELECT 1 FROM attribute_prediction.prediction p WHERE p.run_id = r.run_id)
                ORDER BY r.run_id DESC;
                """
            );
            await using var reader = await command.ExecuteReaderAsync();
            var runs = new List<RunDto>();
            while (await reader.ReadAsync())
            {
                runs.Add(new RunDto(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    $"{reader.GetString(4)} v{reader.GetInt32(5)}",
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetDateTime(7)
                ));
            }
            return runs;
        });

        // Confusion matrix (true class x predicted class) over the hidden partition, plus the class list.
        // Metrics (accuracy, precision/recall/F1, coarse categories) are derived from it client-side.
        api.MapGet("/runs/{runId:int}/evaluation", async (int runId, string partition = "test") =>
        {
            if (!Partitions.Contains(partition)) return Results.BadRequest("partition must be train, val or test");

            var classes = new List<ClassDto>();
            await using (var command = dataSource.CreateCommand(
                """
                SELECT c.class_id, c.name, c.category
                FROM attribute_prediction.attribute_class c
                JOIN attribute_prediction.model_run r USING (attribute_id)
                WHERE r.run_id = @run
                ORDER BY c.class_id;
                """))
            {
                command.Parameters.AddWithValue("run", runId);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    classes.Add(new ClassDto(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
            if (classes.Count == 0) return Results.NotFound();

            var cells = new List<ConfusionCellDto>();
            await using (var command = dataSource.CreateCommand(
                """
                SELECT sl.class_id, p.predicted_class_id, count(*)::int
                FROM attribute_prediction.model_run r
                JOIN attribute_prediction.split_assignment sa
                    ON sa.split_id = r.split_id AND sa.partition = @partition::attribute_prediction.partition
                JOIN attribute_prediction.segment_label sl
                    ON sl.segment_id = sa.segment_id
                    AND sl.attribute_id = r.attribute_id
                    AND sl.label_rule_version = r.label_rule_version
                LEFT JOIN attribute_prediction.prediction p
                    ON p.run_id = r.run_id AND p.segment_id = sa.segment_id
                WHERE r.run_id = @run
                GROUP BY 1, 2;
                """))
            {
                command.Parameters.AddWithValue("run", runId);
                command.Parameters.AddWithValue("partition", partition);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    cells.Add(new ConfusionCellDto(
                        reader.GetInt32(0),
                        reader.IsDBNull(1) ? null : reader.GetInt32(1),
                        reader.GetInt32(2)
                    ));
            }

            return Results.Ok(new EvaluationDto(runId, partition, classes, cells));
        });

        // Mapbox vector tile with every segment in the tile: true class (t), predicted class (p), partition (s).
        // At low zooms short segments are dropped to keep tiles small.
        api.MapGet("/runs/{runId:int}/tiles/{z:int}/{x:int}/{y:int}", async (int runId, int z, int x, int y, HttpContext http) =>
        {
            if (z is < 0 or > 22 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z) return Results.BadRequest();

            double minLength = z switch { <= 10 => 400, 11 => 120, 12 => 40, _ => 0 };

            await using var command = dataSource.CreateCommand(
                """
                WITH bounds AS (
                    SELECT ST_TileEnvelope(@z, @x, @y) AS tile, ST_Transform(ST_TileEnvelope(@z, @x, @y, margin => 0.015625), 4326) AS filter
                ),
                run AS (
                    SELECT run_id, attribute_id, label_rule_version, split_id
                    FROM attribute_prediction.model_run WHERE run_id = @run
                ),
                features AS (
                    SELECT
                        e.fid AS id,
                        sl.class_id AS t,
                        p.predicted_class_id AS p,
                        sa.partition::text AS s,
                        ST_AsMVTGeom(ST_Transform(e.geometry, 3857), b.tile, 4096, 64, true) AS geom
                    FROM osm_edges e
                    CROSS JOIN bounds b
                    CROSS JOIN run r
                    LEFT JOIN attribute_prediction.segment_label sl
                        ON sl.segment_id = e.fid AND sl.attribute_id = r.attribute_id AND sl.label_rule_version = r.label_rule_version
                    LEFT JOIN attribute_prediction.split_assignment sa
                        ON sa.split_id = r.split_id AND sa.segment_id = e.fid
                    LEFT JOIN attribute_prediction.prediction p
                        ON p.run_id = r.run_id AND p.segment_id = e.fid
                    WHERE e.geometry && b.filter AND e.length >= @minLength
                )
                SELECT ST_AsMVT(features, 'segments', 4096, 'geom', 'id') FROM features WHERE geom IS NOT NULL;
                """
            );
            command.Parameters.AddWithValue("run", runId);
            command.Parameters.AddWithValue("z", z);
            command.Parameters.AddWithValue("x", x);
            command.Parameters.AddWithValue("y", y);
            command.Parameters.AddWithValue("minLength", minLength);

            var tile = await command.ExecuteScalarAsync() as byte[];
            // Predictions of a finished run never change, so tiles can be cached by the browser.
            http.Response.Headers.CacheControl = "public, max-age=3600";
            if (tile is null || tile.Length == 0) return Results.NoContent();
            return Results.Bytes(tile, "application/vnd.mapbox-vector-tile");
        });

        // Details for one segment, shown when it is clicked on the map.
        api.MapGet("/runs/{runId:int}/segments/{segmentId:long}", async (int runId, long segmentId) =>
        {
            await using var command = dataSource.CreateCommand(
                """
                SELECT e.fid, e.name, e.highway, e.maxspeed, e.lanes, e.oneway, e.length,
                       sl.class_id, p.predicted_class_id, p.probabilities, sa.partition::text
                FROM osm_edges e
                CROSS JOIN (SELECT * FROM attribute_prediction.model_run WHERE run_id = @run) r
                LEFT JOIN attribute_prediction.segment_label sl
                    ON sl.segment_id = e.fid AND sl.attribute_id = r.attribute_id AND sl.label_rule_version = r.label_rule_version
                LEFT JOIN attribute_prediction.split_assignment sa
                    ON sa.split_id = r.split_id AND sa.segment_id = e.fid
                LEFT JOIN attribute_prediction.prediction p
                    ON p.run_id = r.run_id AND p.segment_id = e.fid
                WHERE e.fid = @segment;
                """
            );
            command.Parameters.AddWithValue("run", runId);
            command.Parameters.AddWithValue("segment", segmentId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return Results.NotFound();

            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            return Results.Ok(new SegmentDto(
                reader.GetInt64(0),
                Text(1),
                Text(2),
                Text(3),
                Text(4),
                Text(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<double[]>(9),
                Text(10)
            ));
        });
    }

    record RunDto(int RunId, string Attribute, string Architecture, string Split, string FeatureSet, string Status, DateTime? StartedAt);

    record ClassDto(int ClassId, string Name, string? Category);

    record ConfusionCellDto(int TrueClass, int? PredictedClass, int Count);

    record EvaluationDto(int RunId, string Partition, List<ClassDto> Classes, List<ConfusionCellDto> Confusion);

    record SegmentDto(
        long Id,
        string? Name,
        string? Highway,
        string? Maxspeed,
        string? Lanes,
        string? Oneway,
        double? Length,
        int? TrueClass,
        int? PredictedClass,
        double[]? Probabilities,
        string? Partition
    );
}
