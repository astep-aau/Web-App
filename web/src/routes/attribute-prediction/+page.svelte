<script lang="ts">
	import { onMount } from 'svelte';
	import SegmentMap, { type ViewMode } from './SegmentMap.svelte';
	import Stats from './Stats.svelte';
	import {
		classColors,
		computeMetrics,
		fetchEvaluation,
		fetchRuns,
		prettyName,
		type Evaluation,
		type Partition,
		type Run
	} from './api.js';

	const sets: { id: Partition; label: string; note: string }[] = [
		{ id: 'train', label: 'Train', note: 'the model learned from these road types' },
		{ id: 'val', label: 'Validation', note: 'held out from training, used to tune the model' },
		{ id: 'test', label: 'Test', note: 'never seen by the model' }
	];

	const modes: { id: ViewMode; label: string }[] = [
		{ id: 'prediction', label: 'Prediction' },
		{ id: 'truth', label: 'Real' },
		{ id: 'errors', label: 'Right / wrong' }
	];

	let runs = $state<Run[]>([]);
	let runId = $state<number>();
	let partition = $state<Partition>('test');
	let mode = $state<ViewMode>('prediction');

	let evaluation = $state<Evaluation>();
	let error = $state<string>();
	let loading = $state(false);

	const run = $derived(runs.find((r) => r.runId === runId));
	const set = $derived(sets.find((s) => s.id === partition)!);
	const colors = $derived(classColors(evaluation?.classes ?? []));
	// Only show numbers that belong to the current run and set, never stale ones from a previous selection.
	const current = $derived(evaluation?.runId === runId && evaluation?.partition === partition ? evaluation : undefined);
	const metrics = $derived(current ? computeMetrics(current) : undefined);

	const help = $derived(
		mode === 'prediction'
			? `${set.label} segments coloured by the road type our model predicted.`
			: mode === 'truth'
				? `${set.label} segments coloured by their real road type from OpenStreetMap.`
				: `Green where the prediction for a ${set.label.toLowerCase()} segment is correct, red where it is wrong.`
	);

	onMount(async () => {
		try {
			runs = await fetchRuns();
			runId = runs[0]?.runId;
			if (runId === undefined) error = 'No model runs with predictions in the database yet.';
		} catch (e) {
			error = `Could not reach the API: ${(e as Error).message}`;
		}
	});

	$effect(() => {
		if (runId === undefined) return;
		const [id, p] = [runId, partition];
		loading = true;
		error = undefined;
		fetchEvaluation(id, p)
			.then((result) => {
				if (id === runId && p === partition) evaluation = result;
			})
			.catch((e) => {
				if (id === runId && p === partition) error = `Could not load the results: ${(e as Error).message}. Try selecting the set again.`;
			})
			.finally(() => (loading = false));
	});
</script>

<svelte:head>
	<title>Attribute Prediction · aSTEP</title>
</svelte:head>

<header class="intro">
	<h1>Attribute Prediction</h1>
	<p class="lede">
		Our model predicts the road type of every road segment in Beijing. The segments are split into a train set
		the model learned from, and validation and test sets it never saw the road types of. Pick a set to compare
		the predictions with the real road types. Click a segment for details.
	</p>
</header>

{#if error}
	<p class="alert" role="alert">{error}</p>
{/if}

<div class="toolbar">
	<div class="field">
		<label for="run">Model run</label>
		<select id="run" bind:value={runId} disabled={!runs.length}>
			{#each runs as r (r.runId)}
				<option value={r.runId}>#{r.runId} · {r.architecture} ({r.status})</option>
			{/each}
		</select>
	</div>

	<div class="field">
		<label for="partition">Set</label>
		<select id="partition" bind:value={partition} aria-describedby="set-note">
			{#each sets as s (s.id)}
				<option value={s.id}>{s.label}</option>
			{/each}
		</select>
	</div>

	<div class="field grow">
		<span class="label" id="view-label">View</span>
		<div class="steps" role="group" aria-labelledby="view-label">
			{#each modes as m (m.id)}
				<button type="button" aria-pressed={mode === m.id} onclick={() => (mode = m.id)}>{m.label}</button>
			{/each}
		</div>
	</div>

	<p class="set-note" id="set-note">
		<strong>{set.label} set:</strong> {set.note}.
	</p>
</div>

<div class="layout">
	<section class="map-panel card" aria-label="Map">
		{#if runId !== undefined && evaluation}
			<SegmentMap {runId} classes={evaluation.classes} {colors} {partition} {mode} />
		{:else if !error}
			<div class="placeholder" aria-busy="true">
				<span class="spinner" aria-hidden="true"></span>
				Loading map…
			</div>
		{/if}

		{#if evaluation}
			<div class="legend">
				<p class="help" aria-live="polite">{help}</p>
				<ul>
					{#if mode === 'errors'}
						<li><span class="line" style:background="var(--map-correct)"></span>Correct</li>
						<li><span class="line" style:background="var(--map-wrong)"></span>Wrong</li>
					{:else}
						{#each evaluation.classes as c (c.classId)}
							<li><span class="line" style:background={colors.get(c.classId)}></span>{prettyName(c.name)}</li>
						{/each}
					{/if}
				</ul>
			</div>
		{/if}
	</section>

	<aside class="stats card" class:loading aria-busy={loading}>
		<div class="stats-header">
			<h2>Results on the {set.label.toLowerCase()} set</h2>
			{#if run}
				<dl class="run-info">
					<div><dt>Model</dt><dd>{run.architecture}</dd></div>
					<div><dt>Features</dt><dd>{run.featureSet}</dd></div>
					<div><dt>Split</dt><dd>{run.split}</dd></div>
				</dl>
			{/if}
		</div>
		{#if metrics}
			<Stats {metrics} {colors} />
		{:else if !error}
			<p class="muted">Loading…</p>
		{/if}
	</aside>
</div>

<style>
	.intro {
		margin-bottom: 20px;
	}

	h1 {
		margin: 0 0 8px;
		font-size: clamp(1.5rem, 2.4vw, 1.875rem);
	}

	.lede {
		margin: 0;
		max-width: 72ch;
		color: var(--color-text-muted);
	}

	.alert {
		margin: 0 0 16px;
		padding: 12px 14px;
		border-radius: var(--radius-md);
		background: var(--color-danger-bg);
		color: var(--color-danger-text);
	}

	.card {
		background: var(--color-surface);
		border: 1px solid var(--color-border);
		border-radius: var(--radius-lg);
		box-shadow: var(--shadow-sm);
	}

	/* Toolbar */
	.toolbar {
		display: flex;
		flex-wrap: wrap;
		align-items: flex-end;
		gap: 16px 20px;
		margin-bottom: 16px;
		padding: 14px 16px;
		background: var(--color-surface);
		border: 1px solid var(--color-border);
		border-radius: var(--radius-lg);
		box-shadow: var(--shadow-sm);
	}

	.field {
		display: flex;
		flex-direction: column;
		gap: 6px;
	}

	.field label,
	.field .label {
		font-size: 0.75rem;
		font-weight: 500;
		color: var(--color-text-muted);
	}

	select {
		min-height: 40px;
		padding: 0 36px 0 12px;
		border: 1px solid var(--color-border-strong);
		border-radius: var(--radius-sm);
		background: var(--color-surface)
			url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%2364748b' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='m6 9 6 6 6-6'/%3E%3C/svg%3E")
			no-repeat right 12px center;
		appearance: none;
		font-size: 0.875rem;
		cursor: pointer;
		transition: border-color var(--duration) ease;
	}

	select:hover {
		border-color: var(--color-text-muted);
	}

	.steps {
		display: inline-flex;
		flex-wrap: wrap;
		gap: 4px;
		padding: 4px;
		border-radius: var(--radius-md);
		background: var(--color-surface-muted);
	}

	.steps button {
		display: inline-flex;
		align-items: center;
		gap: 8px;
		min-height: 36px;
		padding: 0 14px;
		border: 0;
		border-radius: var(--radius-sm);
		background: transparent;
		color: var(--color-text-muted);
		font-size: 0.875rem;
		font-weight: 500;
		cursor: pointer;
		transition:
			background-color var(--duration) ease,
			color var(--duration) ease,
			box-shadow var(--duration) ease;
	}

	.steps button:hover {
		color: var(--color-text);
	}

	.steps button[aria-pressed='true'] {
		background: var(--color-surface);
		color: var(--color-text);
		box-shadow: var(--shadow-sm);
	}

	/* Map + stats */
	.layout {
		display: grid;
		grid-template-columns: minmax(0, 1fr) 400px;
		gap: 16px;
		align-items: start;
	}

	.map-panel {
		position: relative;
		height: calc(100vh - var(--header-height) - 290px);
		min-height: 520px;
		padding: 0;
		overflow: hidden;
	}

	.placeholder {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 10px;
		height: 100%;
		color: var(--color-text-muted);
		font-size: 0.875rem;
	}

	.spinner {
		width: 16px;
		height: 16px;
		border: 2px solid var(--color-border-strong);
		border-top-color: var(--color-primary);
		border-radius: 999px;
		animation: spin 0.8s linear infinite;
	}

	@keyframes spin {
		to {
			transform: rotate(360deg);
		}
	}

	.legend {
		position: absolute;
		left: 12px;
		top: 12px;
		max-width: 280px;
		padding: 12px 14px;
		border: 1px solid var(--color-border);
		border-radius: var(--radius-md);
		background: color-mix(in srgb, var(--color-surface) 94%, transparent);
		backdrop-filter: blur(6px);
		box-shadow: var(--shadow-md);
		font-size: 0.75rem;
	}

	.legend .help {
		margin: 0 0 8px;
		color: var(--color-text);
		line-height: 1.4;
	}

	.legend ul {
		list-style: none;
		margin: 0;
		padding: 0;
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 4px 12px;
		color: var(--color-text-muted);
	}

	.legend li {
		display: flex;
		align-items: center;
	}

	.legend .line {
		display: inline-block;
		flex-shrink: 0;
		width: 18px;
		height: 4px;
		border-radius: 2px;
		margin-right: 8px;
	}

	.set-note {
		flex-basis: 100%;
		margin: -4px 0 0;
		font-size: 0.8125rem;
		color: var(--color-text-muted);
	}

	.set-note strong {
		color: var(--color-text);
		font-weight: 600;
	}

	.stats {
		max-height: calc(100vh - var(--header-height) - 290px);
		min-height: 520px;
		overflow-y: auto;
		padding: 18px;
		transition: opacity var(--duration) ease;
	}

	.stats.loading {
		opacity: 0.55;
	}

	.stats-header {
		margin-bottom: 16px;
	}

	.stats h2 {
		margin: 0 0 10px;
		font-size: 1rem;
	}

	.run-info {
		display: grid;
		gap: 4px;
		margin: 0;
		font-size: 0.8125rem;
	}

	.run-info div {
		display: grid;
		grid-template-columns: 72px 1fr;
		gap: 8px;
	}

	.run-info dt {
		color: var(--color-text-muted);
	}

	.run-info dd {
		margin: 0;
	}

	.muted {
		color: var(--color-text-muted);
	}

	@media (max-width: 1024px) {
		.layout {
			grid-template-columns: 1fr;
		}

		.map-panel {
			height: 65vh;
			min-height: 380px;
		}

		.stats {
			max-height: none;
			min-height: 0;
		}
	}

	@media (max-width: 640px) {
		.toolbar {
			display: grid;
			grid-template-columns: 1fr 1fr;
			gap: 14px 12px;
		}

		.field.grow {
			grid-column: 1 / -1;
		}

		.steps {
			display: grid;
			grid-template-columns: 1fr 1fr;
		}

		select {
			width: 100%;
		}

		.legend {
			left: 8px;
			right: 8px;
			top: auto;
			bottom: 36px;
			max-width: none;
		}

		.legend ul {
			grid-template-columns: repeat(3, auto);
		}
	}
</style>
