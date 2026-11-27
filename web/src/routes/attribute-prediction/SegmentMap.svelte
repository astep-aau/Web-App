<script lang="ts" module>
	export type ViewMode = 'prediction' | 'truth' | 'errors';
</script>

<script lang="ts">
	import { onMount } from 'svelte';
	import 'maplibre-gl/dist/maplibre-gl.css';
	import type { ExpressionSpecification, Map as MapLibreMap, VectorTileSource } from 'maplibre-gl';
	import { fetchSegment, prettyName, tileUrl, type Partition, type RoadClass } from './api.js';

	type Props = {
		runId: number;
		classes: RoadClass[];
		colors: Map<number, string>;
		/** The set (train / val / test) whose segments are shown and evaluated. */
		partition: Partition;
		mode: ViewMode;
	};

	let { runId, classes, colors, partition, mode }: Props = $props();

	const UNLABELLED = '#9ca3af';
	const ESRI = 'https://server.arcgisonline.com/ArcGIS/rest/services/Canvas';

	/** Map colours come from the CSS tokens in app.css so the legend and the map always agree. */
	type Theme = { dark: boolean; correct: string; wrong: string; casing: string };

	function readTheme(dark: boolean): Theme {
		const css = getComputedStyle(document.documentElement);
		const token = (name: string) => css.getPropertyValue(name).trim();
		return {
			dark,
			correct: token('--map-correct'),
			wrong: token('--map-wrong'),
			casing: token('--color-surface')
		};
	}

	let container!: HTMLDivElement;
	let map = $state<MapLibreMap>();
	let theme = $state<Theme>();
	let maplibre: typeof import('maplibre-gl') | undefined;

	const width = (scale: number): ExpressionSpecification => [
		'interpolate', ['exponential', 1.6], ['zoom'],
		10, 0.5 * scale,
		13, 1.4 * scale,
		16, 4 * scale,
		18, 9 * scale
	];

	const colorBy = (property: string): ExpressionSpecification =>
		['match', ['coalesce', ['get', property], -1], ...[...colors].flat(), UNLABELLED] as unknown as ExpressionSpecification;

	const escape = (text: string) => text.replace(/[&<>"']/g, (ch) => `&#${ch.charCodeAt(0)};`);
	const className = (id: number | null) => {
		const c = classes.find((c) => c.classId === id);
		return c ? prettyName(c.name) : '—';
	};
	const swatch = (id: number | null) =>
		`<span class="swatch" style="background:${colors.get(id ?? -1) ?? UNLABELLED}"></span>`;

	onMount(() => {
		let disposed = false;
		const scheme = window.matchMedia('(prefers-color-scheme: dark)');
		const onScheme = () => (theme = readTheme(scheme.matches));
		onScheme();
		scheme.addEventListener('change', onScheme);

		(async () => {
			maplibre = await import('maplibre-gl');
			if (disposed) return;

			const raster = (name: string) => ({
				type: 'raster' as const,
				tiles: [`${ESRI}/${name}/MapServer/tile/{z}/{y}/{x}`],
				tileSize: 256,
				maxzoom: 16,
				attribution: 'Basemap © Esri · Roads © <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
			});

			const instance = new maplibre.Map({
				container,
				center: [116.4074, 39.9042],
				zoom: 12,
				minZoom: 10,
				maxBounds: [[114.9, 39.1], [118.0, 41.4]],
				attributionControl: { compact: true },
				style: {
					version: 8,
					sources: { light: raster('World_Light_Gray_Base'), dark: raster('World_Dark_Gray_Base') },
					layers: [
						{ id: 'basemap-light', type: 'raster', source: 'light', paint: { 'raster-opacity': 0.85 } },
						{ id: 'basemap-dark', type: 'raster', source: 'dark', paint: { 'raster-opacity': 0.9 } }
					]
				}
			});
			instance.addControl(new maplibre.NavigationControl({ showCompass: false }), 'top-right');
			instance.addControl(new maplibre.ScaleControl(), 'bottom-left');

			instance.on('load', () => {
				instance.addSource('segments', { type: 'vector', tiles: [tileUrl(runId)], minzoom: 10, maxzoom: 16 });
				const line = { 'line-cap': 'round', 'line-join': 'round' } as const;
				const base = { type: 'line', source: 'segments', 'source-layer': 'segments', layout: line } as const;

				instance.addLayer({ ...base, id: 'unlabelled', filter: ['!', ['has', 't']], paint: { 'line-color': UNLABELLED, 'line-width': width(0.8), 'line-opacity': 0.15 } });
				instance.addLayer({ ...base, id: 'context', paint: { 'line-width': width(1), 'line-opacity': 0.2 } });
				instance.addLayer({ ...base, id: 'selected-casing', paint: { 'line-width': width(2.6) } });
				instance.addLayer({ ...base, id: 'selected', paint: { 'line-width': width(1.6) } });

				map = instance;
			});

			const around = (p: { x: number; y: number }) =>
				[[p.x - 4, p.y - 4], [p.x + 4, p.y + 4]] as [[number, number], [number, number]];
			const segmentLayers = ['selected', 'context', 'unlabelled'];

			instance.on('mousemove', (e) => {
				const hits = instance.queryRenderedFeatures(around(e.point), { layers: segmentLayers });
				instance.getCanvas().style.cursor = hits.length ? 'pointer' : '';
			});

			instance.on('click', async (e) => {
				const [hit] = instance.queryRenderedFeatures(around(e.point), { layers: segmentLayers });
				if (hit?.id === undefined) return;

				const popup = new maplibre!.Popup({ maxWidth: '300px' })
					.setLngLat(e.lngLat)
					.setHTML('<p class="popup-status">Loading…</p>')
					.addTo(instance);
				try {
					popup.setHTML(describeSegment(await fetchSegment(runId, Number(hit.id))));
				} catch {
					popup.setHTML('<p class="popup-status">Could not load this segment.</p>');
				}
			});
		})();

		return () => {
			disposed = true;
			scheme.removeEventListener('change', onScheme);
			map?.remove();
		};
	});

	function describeSegment(segment: Awaited<ReturnType<typeof fetchSegment>>) {
		const inSet = segment.partition === partition;
		const rows: [string, string][] = [['Real type', `${swatch(segment.trueClass)}${className(segment.trueClass)}`]];
		if (inSet) {
			const ok = segment.trueClass !== null && segment.trueClass === segment.predictedClass;
			rows.push([
				'Predicted',
				`${swatch(segment.predictedClass)}${className(segment.predictedClass)} <span class="badge ${ok ? 'good' : 'bad'}">${ok ? 'Correct' : 'Wrong'}</span>`
			]);
		}
		if (segment.probabilities?.length) {
			// Assumes probabilities are stored in class_id order.
			const top = segment.probabilities
				.map((prob, i) => ({ prob, c: classes[i] }))
				.filter((x) => x.c)
				.sort((a, b) => b.prob - a.prob)
				.slice(0, 3);
			rows.push(['Top classes', top.map((x) => `${prettyName(x.c.name)} <span class="num">${(x.prob * 100).toFixed(0)}%</span>`).join('<br>')]);
		}
		rows.push(['Set', segment.partition ?? 'not labelled']);
		if (segment.length !== null) rows.push(['Length', `<span class="num">${Math.round(segment.length)} m</span>`]);
		if (segment.lanes) rows.push(['Lanes', escape(segment.lanes)]);
		if (segment.maxspeed) rows.push(['Max speed', escape(segment.maxspeed)]);

		return `
			<div class="segment-popup">
				<div class="title"><strong>${segment.name ? escape(segment.name) : 'Unnamed road'}</strong><span class="num">#${segment.id}</span></div>
				<table>${rows.map(([k, v]) => `<tr><th>${k}</th><td>${v}</td></tr>`).join('')}</table>
			</div>`;
	}

	// Run changed → point the segment source at that run's tiles.
	$effect(() => {
		const url = tileUrl(runId);
		(map?.getSource('segments') as VectorTileSource | undefined)?.setTiles([url]);
	});

	// Light/dark switched → swap basemap and theme-dependent colours.
	$effect(() => {
		if (!map || !theme) return;
		map.setLayoutProperty('basemap-light', 'visibility', theme.dark ? 'none' : 'visible');
		map.setLayoutProperty('basemap-dark', 'visibility', theme.dark ? 'visible' : 'none');
		map.setPaintProperty('selected-casing', 'line-color', theme.casing);
	});

	// Set / view mode / colours changed → restyle layers.
	$effect(() => {
		if (!map || !theme) return;
		const inSet: ExpressionSpecification = ['==', ['get', 's'], partition];
		const context: ExpressionSpecification = ['all', ['has', 't'], ['!=', ['get', 's'], partition]];

		map.setFilter('context', context);
		map.setFilter('selected-casing', inSet);
		map.setFilter('selected', inSet);
		map.setPaintProperty('context', 'line-color', mode === 'errors' ? UNLABELLED : colorBy('t'));

		const correctOrWrong: ExpressionSpecification = ['case', ['==', ['get', 't'], ['coalesce', ['get', 'p'], -1]], theme.correct, theme.wrong];

		map.setPaintProperty('selected', 'line-color', mode === 'prediction' ? colorBy('p') : mode === 'truth' ? colorBy('t') : correctOrWrong);
	});
</script>

<div class="map" bind:this={container} role="region" aria-label="Map of road segments"></div>

<style>
	.map {
		width: 100%;
		height: 100%;
		min-height: 420px;
		border-radius: var(--radius-lg);
		overflow: hidden;
		background: var(--color-surface-muted);
	}

	/* MapLibre popups and controls, themed with the shared tokens. */
	.map :global(.maplibregl-popup-content) {
		padding: 12px 14px;
		border-radius: var(--radius-md);
		background: var(--color-surface);
		color: var(--color-text);
		box-shadow: var(--shadow-md);
		font-family: var(--font-sans);
	}

	.map :global(.maplibregl-popup-anchor-bottom .maplibregl-popup-tip),
	.map :global(.maplibregl-popup-anchor-bottom-left .maplibregl-popup-tip),
	.map :global(.maplibregl-popup-anchor-bottom-right .maplibregl-popup-tip) {
		border-top-color: var(--color-surface);
	}

	.map :global(.maplibregl-popup-anchor-top .maplibregl-popup-tip),
	.map :global(.maplibregl-popup-anchor-top-left .maplibregl-popup-tip),
	.map :global(.maplibregl-popup-anchor-top-right .maplibregl-popup-tip) {
		border-bottom-color: var(--color-surface);
	}

	.map :global(.maplibregl-popup-anchor-left .maplibregl-popup-tip) {
		border-right-color: var(--color-surface);
	}

	.map :global(.maplibregl-popup-anchor-right .maplibregl-popup-tip) {
		border-left-color: var(--color-surface);
	}

	.map :global(.maplibregl-popup-close-button) {
		width: 28px;
		height: 28px;
		font-size: 18px;
		color: var(--color-text-muted);
		border-radius: var(--radius-sm);
	}

	.map :global(.maplibregl-popup-close-button:hover) {
		background: var(--color-surface-muted);
	}

	.map :global(.popup-status) {
		margin: 0;
		font-size: 0.8125rem;
		color: var(--color-text-muted);
	}

	.map :global(.segment-popup) {
		font-size: 0.8125rem;
		line-height: 1.45;
	}

	.map :global(.segment-popup .title) {
		display: flex;
		align-items: baseline;
		gap: 6px;
		padding-right: 20px;
		font-size: 0.875rem;
	}

	.map :global(.segment-popup .num) {
		font-family: var(--font-mono);
		font-size: 0.75rem;
	}

	.map :global(.segment-popup .title .num) {
		color: var(--color-text-muted);
	}

	.map :global(.segment-popup table) {
		margin-top: 8px;
		border-collapse: collapse;
	}

	.map :global(.segment-popup th) {
		text-align: left;
		font-weight: 500;
		color: var(--color-text-muted);
		padding: 2px 12px 2px 0;
		vertical-align: top;
		white-space: nowrap;
	}

	.map :global(.badge) {
		display: inline-block;
		margin-left: 4px;
		padding: 0 6px;
		border-radius: 999px;
		font-size: 0.6875rem;
		font-weight: 600;
		line-height: 1.6;
	}

	.map :global(.badge.good) {
		background: color-mix(in srgb, var(--color-good) 16%, transparent);
		color: var(--color-good);
	}

	.map :global(.badge.bad) {
		background: color-mix(in srgb, var(--color-bad) 16%, transparent);
		color: var(--color-bad);
	}

	.map :global(.swatch) {
		display: inline-block;
		width: 10px;
		height: 10px;
		border-radius: 3px;
		margin-right: 6px;
		vertical-align: -1px;
	}

	@media (prefers-color-scheme: dark) {
		.map :global(.maplibregl-ctrl-group) {
			background: var(--color-surface);
		}

		.map :global(.maplibregl-ctrl-group button + button) {
			border-top-color: var(--color-border);
		}

		.map :global(.maplibregl-ctrl-icon) {
			filter: invert(1);
		}

		.map :global(.maplibregl-ctrl-attrib),
		.map :global(.maplibregl-ctrl-scale) {
			background: color-mix(in srgb, var(--color-surface) 80%, transparent);
			color: var(--color-text-muted);
		}

		.map :global(.maplibregl-ctrl-attrib a) {
			color: var(--color-text);
		}
	}
</style>
