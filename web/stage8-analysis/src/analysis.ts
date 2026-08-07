import type {
  AnalysisData,
  FilterSlice,
  HeatmapCell,
  HeatmapMode,
  LocationBasis,
  Rate,
  SpeedFilter,
  StyleFilter,
} from "./types";

const number = new Intl.NumberFormat("en-US");

export function validateAnalysis(data: AnalysisData): void {
  const failures: string[] = [];
  if (data.schema_id !== "goalkeeper-analysis-v1") failures.push("schema");
  if (data.policies.length !== 2) failures.push("policies");
  if (data.goal_grid.columns !== 4 || data.goal_grid.rows !== 3) failures.push("grid");
  if (data.goal_grid.cell_order.length !== 12) failures.push("cell order");
  if (data.filter_slices.length !== 32) failures.push("filter slices");
  if (data.overall_policy_rows.length !== 2) failures.push("overall rows");
  if (data.breakdown_tables.length !== 4) failures.push("breakdown tables");
  if (data.safety_totals.some((row) => row.total_failures !== 0)) failures.push("safety");

  const keys = new Set<string>();
  for (const slice of data.filter_slices) {
    const key = `${slice.location_basis}|${slice.style_filter}|${slice.speed_filter}`;
    if (keys.has(key)) failures.push(`duplicate ${key}`);
    keys.add(key);
    if (slice.cells.length !== 12) failures.push(`cells ${key}`);
    if (slice.cells.reduce((sum, cell) => sum + cell.sample_count, 0) !== slice.sample_count) {
      failures.push(`sample count ${key}`);
    }
  }
  if (failures.length) throw new Error(`Invalid goalkeeper analysis: ${failures.join(", ")}`);
}

export function findSlice(
  data: AnalysisData,
  location: LocationBasis,
  style: StyleFilter,
  speed: SpeedFilter,
): FilterSlice {
  const slice = data.filter_slices.find(
    (candidate) =>
      candidate.location_basis === location &&
      candidate.style_filter === style &&
      candidate.speed_filter === speed,
  );
  if (!slice) throw new Error(`Missing analysis slice: ${location}/${style}/${speed}`);
  return slice;
}

export function percent(value: number, digits = 1): string {
  return `${(value * 100).toFixed(digits)}%`;
}

export function points(value: number): string {
  if (Math.abs(value) < 0.05) return "0.0 pp";
  return `${value > 0 ? "+" : ""}${value.toFixed(1)} pp`;
}

export function count(value: number): string {
  return number.format(value);
}

export function interval(rate: Rate): string {
  return `${percent(rate.ci95_low)}–${percent(rate.ci95_high)}`;
}

export function label(value: string): string {
  return value
    .replaceAll("_", " ")
    .replaceAll("-", " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

type CellTheme = { background: string; foreground: string; accent: string };

function mix(start: [number, number, number], end: [number, number, number], amount: number) {
  const value = Math.max(0, Math.min(1, amount));
  return start.map((channel, index) =>
    Math.round(channel + (end[index] - channel) * value),
  ) as [number, number, number];
}

function rgb(value: [number, number, number]): string {
  return `rgb(${value[0]} ${value[1]} ${value[2]})`;
}

export function cellTheme(mode: HeatmapMode, cell: HeatmapCell): CellTheme {
  if (cell.sample_count === 0) {
    return { background: "#eef1ed", foreground: "#5d675f", accent: "#a9b1ab" };
  }
  if (mode === "final") {
    const low: [number, number, number] = [171, 75, 69];
    const middle: [number, number, number] = [210, 165, 55];
    const high: [number, number, number] = [29, 126, 108];
    const color = cell.final_save_rate.value < 0.5
      ? mix(low, middle, cell.final_save_rate.value / 0.5)
      : mix(middle, high, (cell.final_save_rate.value - 0.5) / 0.5);
    return { background: rgb(color), foreground: "#ffffff", accent: "rgba(255,255,255,.72)" };
  }
  const amount = Math.min(1, Math.abs(cell.teacher_gap_points) / 6);
  const neutral: [number, number, number] = [92, 103, 97];
  const positive: [number, number, number] = [177, 73, 67];
  const negative: [number, number, number] = [36, 113, 145];
  const color = mix(neutral, cell.teacher_gap_points >= 0 ? positive : negative, amount);
  return { background: rgb(color), foreground: "#ffffff", accent: "rgba(255,255,255,.72)" };
}
