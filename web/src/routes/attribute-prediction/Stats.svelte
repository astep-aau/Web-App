<script lang="ts">
	import { prettyName, type Metrics } from './api.js';

	let { metrics, colors }: { metrics: Metrics; colors: Map<number, string> } = $props();

	const pct = (x: number, digits = 1) => `${(x * 100).toFixed(digits)}%`;
	const num = (x: number) => x.toLocaleString('en-US');

	// Row-normalised confusion matrix: each cell is the share of that real class predicted as the column class.
	const rowShares = $derived(
		metrics.matrix.map((row, i) => {
			const support = metrics.perClass[i].support;
			return row.map((count) => (support ? count / support : 0));
		})
	);

	const short = (name: string) =>
		({ living_street: 'living', unclassified: 'unclass.', residential: 'resid.', secondary: 'second.' })[name] ?? name;
</script>

<div class="kpis">
	<div class="kpi">
		<span class="label">Segments</span>
		<span class="value">{num(metrics.total)}</span>
		<span class="sub">in this set</span>
	</div>
	<div class="kpi">
		<span class="label">Accuracy</span>
		<span class="value">{pct(metrics.accuracy)}</span>
		<span class="sub">{num(metrics.correct)} correct</span>
	</div>
	<div class="kpi">
		<span class="label">Macro F1</span>
		<span class="value">{metrics.macroF1.toFixed(3)}</span>
		<span class="sub">mean over road types</span>
	</div>
	<div class="kpi">
		<span class="label">Category accuracy</span>
		<span class="value">{pct(metrics.categoryAccuracy)}</span>
		<span class="sub">highway / main / minor</span>
	</div>
</div>

{#if metrics.missing}
	<p class="warning">{num(metrics.missing)} segments have no prediction and count as wrong.</p>
{/if}

<h3>Per road type</h3>
<div class="table-scroll">
	<table class="per-class">
		<thead>
			<tr>
				<th scope="col">Road type</th>
				<th scope="col" class="n">Real</th>
				<th scope="col" class="n">Prec.</th>
				<th scope="col" class="n">Recall</th>
				<th scope="col">F1</th>
			</tr>
		</thead>
		<tbody>
			{#each metrics.perClass as c (c.roadClass.classId)}
				<tr>
					<th scope="row">
						<span class="swatch" style:background={colors.get(c.roadClass.classId)}></span>
						{prettyName(c.roadClass.name)}
					</th>
					<td class="n">{num(c.support)}</td>
					<td class="n">{pct(c.precision, 0)}</td>
					<td class="n">{pct(c.recall, 0)}</td>
					<td class="f1">
						<span class="meter" aria-hidden="true">
							<span style:width="{c.f1 * 100}%" style:background={colors.get(c.roadClass.classId)}></span>
						</span>
						<span class="f1-value">{c.f1.toFixed(2)}</span>
					</td>
				</tr>
			{/each}
		</tbody>
	</table>
</div>

<h3>Confusion matrix</h3>
<p class="hint">
	Rows are the real road type, columns the predicted one. Each cell shows the share (%) of that real type;
	<span class="key good">green</span> is correct, <span class="key bad">red</span> is a mix-up.
</p>
<div class="table-scroll">
	<table class="matrix">
		<thead>
			<tr>
				<th scope="col"><span class="visually-hidden">Real \ Predicted</span></th>
				{#each metrics.perClass as c (c.roadClass.classId)}
					<th scope="col" class="col" title={prettyName(c.roadClass.name)}>{short(c.roadClass.name)}</th>
				{/each}
			</tr>
		</thead>
		<tbody>
			{#each metrics.perClass as row, i (row.roadClass.classId)}
				<tr>
					<th scope="row" class="row">{short(row.roadClass.name)}</th>
					{#each rowShares[i] as share, j (j)}
						<td
							class:diagonal={i === j}
							class:strong={share > 0.5}
							style:--share={share}
							title="{prettyName(row.roadClass.name)} → {prettyName(metrics.perClass[j].roadClass.name)}: {num(
								metrics.matrix[i][j]
							)} ({pct(share)})"
						>
							{share >= 0.005 ? Math.round(share * 100) : ''}
						</td>
					{/each}
				</tr>
			{/each}
		</tbody>
	</table>
</div>

<style>
	.kpis {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 8px;
	}

	.kpi {
		display: flex;
		flex-direction: column;
		gap: 2px;
		padding: 12px 14px;
		border: 1px solid var(--color-border);
		border-radius: var(--radius-md);
		background: var(--color-surface-muted);
	}

	.kpi .label {
		font-size: 0.75rem;
		font-weight: 500;
		color: var(--color-text-muted);
	}

	.kpi .value {
		font-family: var(--font-mono);
		font-size: 1.5rem;
		font-weight: 600;
		font-variant-numeric: tabular-nums;
		letter-spacing: -0.02em;
	}

	.kpi .sub {
		font-size: 0.75rem;
		color: var(--color-text-muted);
	}

	.warning {
		margin: 12px 0 0;
		padding: 8px 12px;
		border-radius: var(--radius-sm);
		background: var(--color-warning-bg);
		color: var(--color-warning-text);
		font-size: 0.8125rem;
	}

	h3 {
		margin: 24px 0 8px;
		font-size: 0.875rem;
	}

	.hint {
		margin: 0 0 10px;
		font-size: 0.75rem;
		line-height: 1.45;
		color: var(--color-text-muted);
	}

	.key {
		font-weight: 600;
	}

	.key.good {
		color: var(--color-good);
	}

	.key.bad {
		color: var(--color-bad);
	}

	.table-scroll {
		overflow-x: auto;
	}

	table {
		border-collapse: collapse;
		width: 100%;
		font-size: 0.8125rem;
	}

	.per-class thead th {
		padding: 6px;
		border-bottom: 1px solid var(--color-border);
		font-size: 0.75rem;
		font-weight: 500;
		text-align: left;
		color: var(--color-text-muted);
	}

	.per-class tbody th,
	.per-class td {
		padding: 6px;
		border-bottom: 1px solid var(--color-border);
		white-space: nowrap;
	}

	.per-class tbody th {
		font-weight: 400;
		text-align: left;
	}

	.per-class tbody tr:last-child > * {
		border-bottom: 0;
	}

	.per-class tbody tr {
		transition: background-color var(--duration) ease;
	}

	.per-class tbody tr:hover {
		background: var(--color-surface-muted);
	}

	.n {
		text-align: right !important;
		font-family: var(--font-mono);
		font-size: 0.75rem;
		font-variant-numeric: tabular-nums;
	}

	.swatch {
		display: inline-block;
		width: 10px;
		height: 10px;
		border-radius: 3px;
		margin-right: 6px;
		vertical-align: -1px;
	}

	.f1 {
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.meter {
		position: relative;
		flex: 1;
		min-width: 32px;
		height: 6px;
		border-radius: 999px;
		background: var(--color-surface-muted);
		overflow: hidden;
	}

	.meter span {
		position: absolute;
		inset: 0 auto 0 0;
		border-radius: 999px;
	}

	.f1-value {
		font-family: var(--font-mono);
		font-size: 0.75rem;
		font-variant-numeric: tabular-nums;
	}

	.matrix {
		table-layout: fixed;
		font-size: 0.6875rem;
	}

	.matrix th {
		font-weight: 500;
		color: var(--color-text-muted);
	}

	.matrix th.col {
		height: 64px;
		padding: 4px 0;
		vertical-align: bottom;
		writing-mode: vertical-rl;
		transform: rotate(180deg);
		text-align: left;
	}

	.matrix th.row {
		width: 64px;
		padding-right: 8px;
		text-align: right;
		white-space: nowrap;
	}

	.matrix td {
		height: 28px;
		border: 2px solid var(--color-surface);
		border-radius: 4px;
		text-align: center;
		font-family: var(--font-mono);
		font-variant-numeric: tabular-nums;
		color: var(--color-text);
		background: color-mix(in srgb, var(--color-bad) calc(var(--share) * 85%), var(--color-surface-muted));
	}

	.matrix td.diagonal {
		font-weight: 600;
		background: color-mix(in srgb, var(--color-good) calc(var(--share) * 85%), var(--color-surface-muted));
	}

	.matrix td.strong {
		color: #fff;
	}

	.visually-hidden {
		position: absolute;
		width: 1px;
		height: 1px;
		overflow: hidden;
		clip-path: inset(50%);
		white-space: nowrap;
	}
</style>
