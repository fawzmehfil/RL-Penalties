export type Rate = {
  successes: number;
  total: number;
  value: number;
  ci95_low: number;
  ci95_high: number;
};

export type Policy = {
  policy_id: string;
  display_name: string;
  role: "final" | "teacher";
};

export type HeatmapCell = {
  cell_id: string;
  sample_count: number;
  final_save_rate: Rate;
  teacher_save_rate: Rate;
  final_glove_contact_rate: Rate;
  teacher_gap_points: number;
};

export type FilterSlice = {
  location_basis: LocationBasis;
  style_filter: StyleFilter;
  speed_filter: SpeedFilter;
  sample_count: number;
  cells: HeatmapCell[];
};

export type OverallPolicyRow = {
  policy_id: string;
  attempts: number;
  all_attempts: number;
  saves: number;
  goals: number;
  save_rate: Rate;
  goal_rate: Rate;
  glove_contact_rate: Rate;
  glove_save_rate: Rate;
  contact_then_goal_rate: Rate;
  invalid_count: number;
  timeout_count: number;
  inference_error_count: number;
};

export type BreakdownPolicy = {
  policy_id: string;
  attempts: number;
  save_rate: Rate;
  glove_contact_rate: Rate;
};

export type BreakdownRow = {
  band_id: string;
  policies: BreakdownPolicy[];
};

export type BreakdownTable = {
  dimension: "height" | "shot_style" | "speed" | "spin";
  rows: BreakdownRow[];
};

export type LeftRightRow = {
  policy_id: string;
  left: { attempts: number; save_rate: Rate };
  right: { attempts: number; save_rate: Rate };
  left_minus_right_points: number;
};

export type AnalysisData = {
  schema_id: string;
  analysis_id: string;
  source_benchmark_id: string;
  generated_at: string;
  master_seed: number;
  episode_key_digest: string;
  source_hashes: { artifact_id: string; sha256: string }[];
  contracts: { contract_id: string; value: string }[];
  policies: Policy[];
  goal_grid: {
    columns: number;
    rows: number;
    minimum_x: number;
    maximum_x: number;
    minimum_y: number;
    maximum_y: number;
    cell_order: string[];
  };
  filter_slices: FilterSlice[];
  overall_policy_rows: OverallPolicyRow[];
  breakdown_tables: BreakdownTable[];
  left_right_rows: LeftRightRow[];
  safety_totals: {
    policy_id: string;
    total_failures: number;
    counts: { metric_id: string; count: number }[];
  }[];
};

export type HeatmapMode = "final" | "teacher-gap";
export type LocationBasis = "intended_target" | "unopposed_crossing";
export type StyleFilter = "all" | "placed" | "power" | "curled";
export type SpeedFilter = "all" | "slow" | "medium" | "fast";
