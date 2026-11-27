<script lang="ts">
  import { onMount } from "svelte";
  import "leaflet/dist/leaflet.css";

  let mapElement!: HTMLDivElement;

  onMount(async () => {
    const leaflet = await import("leaflet");

    const map = leaflet.map(mapElement).setView([39.9042, 116.4074], 10);

    leaflet
      .tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png")
      .addTo(map);

    for (let page = 0; ; page++) {
      const response = await fetch(`${import.meta.env.VITE_API_URL ?? ""}/api/edges?page=${page}`);
      if (!response.ok) {
        throw new Error(`API error: ${response.status}`);
      }
      if (response.status === 204) break;

      const edges = await response.json();
      leaflet.geoJSON(edges).addTo(map);
    }
  });
</script>

<svelte:head>
  <title>Road Network · aSTEP</title>
</svelte:head>

<header class="page-header">
  <h1>Beijing China</h1>
  <p>OpenStreetMap road segments loaded from the shared dataset.</p>
</header>

<div class="map" bind:this={mapElement}></div>

<style>
  .page-header {
    margin-bottom: 16px;
  }

  h1 {
    margin: 0 0 4px;
    font-size: 1.5rem;
  }

  p {
    margin: 0;
    color: var(--color-text-muted);
  }

  .map {
    width: 100%;
    height: calc(100vh - var(--header-height) - 160px);
    min-height: 420px;
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
    box-shadow: var(--shadow-sm);
    overflow: hidden;
  }
</style>
