export type Partition = 'train' | 'val' | 'test';

export type Run = {
	runId: number;
	attribute: string;
	architecture: string;
	split: string;
	featureSet: string;
	status: string;
	startedAt: string | null;
};

export type RoadClass = { classId: number; name: string; category: string | null };

export type Evaluation = {
	runId: number;
	partition: Partition;
	classes: RoadClass[];
	confusion: { trueClass: number; predictedClass: number | null; count: number }[];
};

export type Segment = {
	id: number;
	name: string | null;
	highway: string | null;
	maxspeed: string | null;
	lanes: string | null;
	oneway: string | null;
	length: number | null;
	trueClass: number | null;
	predictedClass: number | null;
	probabilities: number[] | null;
	partition: Partition | null;
};

const apiBase: string = import.meta.env.VITE_API_URL ?? '';

async function get<T>(path: string): Promise<T> {
	const response = await fetch(`${apiBase}/api/attribute-prediction${path}`);
	if (!response.ok) throw new Error(`API error ${response.status} for ${path}`);
	return response.json();
}

export const fetchRuns = () => get<Run[]>('/runs');

export const fetchEvaluation = (runId: number, partition: Partition) =>
	get<Evaluation>(`/runs/${runId}/evaluation?partition=${partition}`);

export const fetchSegment = (runId: number, segmentId: number) =>
	get<Segment>(`/runs/${runId}/segments/${segmentId}`);

/** MapLibre needs an absolute URL template for vector tiles. */
export const tileUrl = (runId: number) =>
	`${apiBase || location.origin}/api/attribute-prediction/runs/${runId}/tiles/{z}/{x}/{y}`;

/** One colour per road class, in class order (motorway → living_street). */
const palette = ['#d62839', '#f26a1b', '#f2b134', '#7fb800', '#0d9c8a', '#2b7bba', '#7048b8', '#d55fb0'];

export const classColors = (classes: RoadClass[]) =>
	new Map(classes.map((c, i) => [c.classId, palette[i % palette.length]]));

export const prettyName = (name: string) => name.replaceAll('_', ' ');

export type ClassMetrics = {
	roadClass: RoadClass;
	support: number;
	predicted: number;
	precision: number;
	recall: number;
	f1: number;
};

export type Metrics = {
	total: number;
	correct: number;
	missing: number;
	accuracy: number;
	macroF1: number;
	categoryAccuracy: number;
	perClass: ClassMetrics[];
	/** matrix[trueIndex][predictedIndex], in class order. */
	matrix: number[][];
};

/** Derives the headline metrics from the confusion matrix returned by the API. */
export function computeMetrics(evaluation: Evaluation): Metrics {
	const { classes } = evaluation;
	const index = new Map(classes.map((c, i) => [c.classId, i]));
	const category = new Map(classes.map((c) => [c.classId, c.category]));
	const matrix = classes.map(() => classes.map(() => 0));
	const support = classes.map(() => 0);

	let total = 0;
	let missing = 0;
	let sameCategory = 0;

	for (const { trueClass, predictedClass, count } of evaluation.confusion) {
		const t = index.get(trueClass);
		if (t === undefined) continue;
		total += count;
		support[t] += count;
		const p = predictedClass === null ? undefined : index.get(predictedClass);
		if (p === undefined) {
			missing += count;
			continue;
		}
		matrix[t][p] += count;
		if (category.get(trueClass) === category.get(predictedClass!)) sameCategory += count;
	}

	const perClass = classes.map((roadClass, i) => {
		const tp = matrix[i][i];
		const predicted = matrix.reduce((sum, row) => sum + row[i], 0);
		const precision = predicted ? tp / predicted : 0;
		const recall = support[i] ? tp / support[i] : 0;
		const f1 = precision + recall ? (2 * precision * recall) / (precision + recall) : 0;
		return { roadClass, support: support[i], predicted, precision, recall, f1 };
	});

	const correct = matrix.reduce((sum, row, i) => sum + row[i], 0);
	const present = perClass.filter((c) => c.support > 0);

	return {
		total,
		correct,
		missing,
		accuracy: total ? correct / total : 0,
		macroF1: present.length ? present.reduce((sum, c) => sum + c.f1, 0) / present.length : 0,
		categoryAccuracy: total ? sameCategory / total : 0,
		perClass,
		matrix
	};
}
