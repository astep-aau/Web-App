<script lang="ts">
	import '../app.css';
	import { resolve } from '$app/paths';
	import { page } from '$app/state';
	import { groups } from '$lib/groups.js';

	let { children } = $props();

	const isActive = (href: string) =>
		page.url.pathname === href || page.url.pathname.startsWith(href + '/');
</script>

<svelte:head>
	<title>aSTEP Project</title>
</svelte:head>

<a class="skip-link" href="#content">Skip to content</a>

<header>
	<div class="bar">
		<a class="brand" href={resolve('/')} aria-label="aSTEP Project home">
			<span class="name">aSTEP</span>
		</a>

		<nav aria-label="Groups">
			{#each groups as group (group.path)}
				{@const href = resolve(group.path)}
				{@const active = isActive(href)}
				<a {href} class:active aria-current={active ? 'page' : undefined}>{group.name}</a>
			{/each}
		</nav>
	</div>
</header>

<main id="content">
	{@render children()}
</main>

<style>
	.skip-link {
		position: absolute;
		left: 8px;
		top: -48px;
		z-index: 100;
		padding: 8px 12px;
		border-radius: var(--radius-sm);
		background: var(--color-primary);
		color: var(--color-on-primary);
		text-decoration: none;
	}

	.skip-link:focus {
		top: 8px;
	}

	header {
		position: sticky;
		top: 0;
		z-index: 50;
		background: color-mix(in srgb, var(--color-surface) 88%, transparent);
		backdrop-filter: saturate(1.4) blur(10px);
		border-bottom: 1px solid var(--color-border);
	}

	.bar {
		display: flex;
		align-items: stretch;
		gap: 24px;
		height: var(--header-height);
		padding: 0 var(--page-gutter);
	}

	.brand {
		display: flex;
		align-items: center;
		flex-shrink: 0;
		color: var(--color-text);
		text-decoration: none;
	}

	.name {
		font-family: var(--font-mono);
		font-weight: 600;
		font-size: 1rem;
		letter-spacing: -0.02em;
	}

	nav {
		display: flex;
		align-items: stretch;
		gap: 4px;
		overflow-x: auto;
		scrollbar-width: none;
	}

	nav a {
		position: relative;
		display: flex;
		align-items: center;
		padding: 0 12px;
		color: var(--color-text-muted);
		font-size: 0.9375rem;
		font-weight: 500;
		text-decoration: none;
		white-space: nowrap;
		transition: color var(--duration) ease;
	}

	nav a::after {
		content: '';
		position: absolute;
		left: 12px;
		right: 12px;
		bottom: -1px;
		height: 2px;
		border-radius: 2px;
		background: var(--color-primary);
		transform: scaleX(0);
		transition: transform var(--duration) ease;
	}

	nav a:hover {
		color: var(--color-text);
	}

	nav a.active {
		color: var(--color-primary);
	}

	nav a.active::after {
		transform: scaleX(1);
	}

	nav a:focus-visible {
		outline-offset: -4px;
	}

	@media (max-width: 520px) {
		.bar {
			gap: 8px;
		}

		nav a {
			padding: 0 10px;
			font-size: 0.875rem;
		}
	}

	main {
		padding: 24px var(--page-gutter) 32px;
		max-width: 1680px;
		margin: 0 auto;
	}
</style>
