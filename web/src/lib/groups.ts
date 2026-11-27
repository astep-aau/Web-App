import type { Pathname } from '$app/types';

export type Group = {
	/** Route of the group's page; must match a folder in src/routes (type-checked). */
	path: Pathname;
	/** Label shown in the tab bar. */
	name: string;
	/** One-liner shown on the landing page. */
	description: string;
};

/**
 * One entry per group. To add your group:
 *   1. Create src/routes/<your-group>/+page.svelte
 *   2. Add an entry below with path '/<your-group>'
 */
export const groups: Group[] = [
	{
		path: '/road-network',
		name: 'Road Network',
		description: 'Beijing road network (OSM edges) rendered on a Leaflet map.'
	},
	{
		path: '/attribute-prediction',
		name: 'Attribute Prediction',
		description: 'Attribute prediction.'
	}
];
