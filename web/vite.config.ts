import adapter from '@sveltejs/adapter-node';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [
		sveltekit({
			paths: { base: '/webapp' },
			compilerOptions: {
				// Force runes mode for the project, except for libraries. Can be removed in svelte 6.
				runes: ({ filename }) =>
					filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},

			adapter: adapter()
		})
	],
	server: {
		// In `npm run dev`, forward /api to the local API on :8080 (`dotnet run` or `docker compose up api`),
		// mirroring how the ingress routes /api on the server.
		proxy: {
			'/api': 'http://localhost:8080'
		}
	}
});
