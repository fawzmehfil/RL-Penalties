import { useMemo, useState } from "react";
import artifact from "../public/data/goalkeeper-analysis-v1.json";
import {
  cellTheme,
  count,
  findSlice,
  interval,
  label,
  percent,
  points,
  validateAnalysis,
} from "./analysis";
import type {
  AnalysisData,
  BreakdownTable as BreakdownTableType,
  HeatmapCell as HeatmapCellType,
  HeatmapMode,
  LocationBasis,
  OverallPolicyRow,
  Policy,
  SpeedFilter,
  StyleFilter,
} from "./types";

const bundledAnalysis = artifact as AnalysisData;

function RateWithInterval({ row }: { row: { save_rate: OverallPolicyRow["save_rate"] } }) {
  return (
    <span className="rate-with-interval">
      <strong>{percent(row.save_rate.value)}</strong>
      <small>{interval(row.save_rate)}</small>
    </span>
  );
}

function HeatmapCell({ cell, mode }: { cell: HeatmapCellType; mode: HeatmapMode }) {
  const theme = cellTheme(mode, cell);
  const style = {
    background: theme.background,
    color: theme.foreground,
    "--cell-accent": theme.accent,
  } as React.CSSProperties;
  if (cell.sample_count === 0) {
    return (
      <article className="heatmap-cell is-empty" style={style} data-testid="heatmap-cell">
        <span className="cell-location">{label(cell.cell_id)}</span>
        <strong className="cell-primary">No data</strong>
        <span className="cell-note">No fixed shots match these filters</span>
      </article>
    );
  }
  const isGap = mode === "teacher-gap";
  return (
    <article className="heatmap-cell" style={style} data-testid="heatmap-cell">
      <div className="cell-topline">
        <span className="cell-location">{label(cell.cell_id)}</span>
        <span className="cell-count">n = {count(cell.sample_count)}</span>
      </div>
      <strong className="cell-primary">
        {isGap ? points(cell.teacher_gap_points) : percent(cell.final_save_rate.value)}
      </strong>
      {isGap ? (
        <div className="cell-comparison">
          <span>Final <b>{percent(cell.final_save_rate.value)}</b></span>
          <span>Teacher <b>{percent(cell.teacher_save_rate.value)}</b></span>
        </div>
      ) : (
        <span className="cell-ci">95% CI {interval(cell.final_save_rate)}</span>
      )}
      <span className="cell-glove">Glove contact {percent(cell.final_glove_contact_rate.value)}</span>
      {isGap && (
        <span className="cell-ci compact">
          CI final {interval(cell.final_save_rate)} · teacher {interval(cell.teacher_save_rate)}
        </span>
      )}
    </article>
  );
}

function OverallTable({ data }: { data: AnalysisData }) {
  const policies = new Map(data.policies.map((policy) => [policy.policy_id, policy]));
  return (
    <div className="table-scroll">
      <table>
        <thead>
          <tr>
            <th>Policy</th>
            <th>Shots</th>
            <th>Saves</th>
            <th>Goals</th>
            <th>Save rate (95% CI)</th>
            <th>Glove contact</th>
            <th>Glove saves</th>
            <th>Contact → goal</th>
          </tr>
        </thead>
        <tbody>
          {data.overall_policy_rows.map((row) => (
            <tr key={row.policy_id}>
              <th>{policies.get(row.policy_id)?.display_name}</th>
              <td>{count(row.attempts)}</td>
              <td>{count(row.saves)}</td>
              <td>{count(row.goals)}</td>
              <td><RateWithInterval row={row} /></td>
              <td>{percent(row.glove_contact_rate.value)}</td>
              <td>{percent(row.glove_save_rate.value)}</td>
              <td>{percent(row.contact_then_goal_rate.value)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function BreakdownTable({ table, policies }: { table: BreakdownTableType; policies: Policy[] }) {
  const final = policies.find((policy) => policy.role === "final")!;
  const teacher = policies.find((policy) => policy.role === "teacher")!;
  return (
    <section className="breakdown-block">
      <h3>{label(table.dimension)}</h3>
      <div className="table-scroll">
        <table className="compact-table">
          <thead>
            <tr>
              <th>Band</th>
              <th>Final goalkeeper</th>
              <th>Reactive teacher</th>
            </tr>
          </thead>
          <tbody>
            {table.rows.map((row) => {
              const finalRow = row.policies.find((policy) => policy.policy_id === final.policy_id)!;
              const teacherRow = row.policies.find((policy) => policy.policy_id === teacher.policy_id)!;
              return (
                <tr key={row.band_id}>
                  <th>{label(row.band_id)}</th>
                  <td>
                    <strong>{percent(finalRow.save_rate.value)}</strong>
                    <small>n {count(finalRow.attempts)} · CI {interval(finalRow.save_rate)}</small>
                  </td>
                  <td>
                    <strong>{percent(teacherRow.save_rate.value)}</strong>
                    <small>n {count(teacherRow.attempts)} · CI {interval(teacherRow.save_rate)}</small>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function IntegrityTable({ data }: { data: AnalysisData }) {
  const policies = new Map(data.policies.map((policy) => [policy.policy_id, policy]));
  return (
    <div className="table-scroll">
      <table className="compact-table integrity-table">
        <thead>
          <tr><th>Policy</th><th>Invalids</th><th>Timeouts</th><th>Inference errors</th><th>All contract failures</th></tr>
        </thead>
        <tbody>
          {data.overall_policy_rows.map((row) => {
            const safety = data.safety_totals.find((item) => item.policy_id === row.policy_id)!;
            return (
              <tr key={row.policy_id}>
                <th>{policies.get(row.policy_id)?.display_name}</th>
                <td>{row.invalid_count}</td>
                <td>{row.timeout_count}</td>
                <td>{row.inference_error_count}</td>
                <td><span className="status-ok">{safety.total_failures}</span></td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function LeftRightTable({ data }: { data: AnalysisData }) {
  const policies = new Map(data.policies.map((policy) => [policy.policy_id, policy]));
  return (
    <div className="table-scroll">
      <table className="compact-table">
        <thead>
          <tr><th>Policy</th><th>Left</th><th>Right</th><th>Left − right</th></tr>
        </thead>
        <tbody>
          {data.left_right_rows.map((row) => (
            <tr key={row.policy_id}>
              <th>{policies.get(row.policy_id)?.display_name}</th>
              <td><strong>{percent(row.left.save_rate.value)}</strong><small>n {count(row.left.attempts)} · CI {interval(row.left.save_rate)}</small></td>
              <td><strong>{percent(row.right.save_rate.value)}</strong><small>n {count(row.right.attempts)} · CI {interval(row.right.save_rate)}</small></td>
              <td><strong>{points(row.left_minus_right_points)}</strong></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function AnalysisDashboard({ data }: { data: AnalysisData }) {
  const [mode, setMode] = useState<HeatmapMode>("final");
  const [location, setLocation] = useState<LocationBasis>("intended_target");
  const [style, setStyle] = useState<StyleFilter>("all");
  const [speed, setSpeed] = useState<SpeedFilter>("all");
  const slice = useMemo(() => findSlice(data, location, style, speed), [data, location, style, speed]);
  const finalRow = data.overall_policy_rows.find((row) =>
    data.policies.find((policy) => policy.policy_id === row.policy_id)?.role === "final",
  )!;
  const teacherRow = data.overall_policy_rows.find((row) =>
    data.policies.find((policy) => policy.policy_id === row.policy_id)?.role === "teacher",
  )!;

  return (
    <>
      <header className="page-header">
        <div className="header-inner">
          <div>
            <p className="eyebrow">Penalty Shootout RL · Stage 8</p>
            <h1>Goalkeeper analysis</h1>
            <p className="header-copy">A fixed-shot view of where the selected goalkeeper succeeds, and where its reactive teacher still leads.</p>
          </div>
          <dl className="benchmark-meta">
            <div><dt>Benchmark</dt><dd>20,000 attempts / policy</dd></div>
            <div><dt>Population</dt><dd>{count(finalRow.attempts)} on-target shots</dd></div>
            <div><dt>Seed</dt><dd>{data.master_seed}</dd></div>
          </dl>
        </div>
      </header>

      <main>
        <section className="summary-strip" aria-label="Overall benchmark summary">
          <div><span>Final goalkeeper</span><strong>{percent(finalRow.save_rate.value)}</strong><small>CI {interval(finalRow.save_rate)}</small></div>
          <div><span>Reactive teacher</span><strong>{percent(teacherRow.save_rate.value)}</strong><small>CI {interval(teacherRow.save_rate)}</small></div>
          <div><span>Teacher gap</span><strong>{points((teacherRow.save_rate.value - finalRow.save_rate.value) * 100)}</strong><small>Teacher − final</small></div>
          <div><span>Final glove contact</span><strong>{percent(finalRow.glove_contact_rate.value)}</strong><small>{count(finalRow.glove_contact_rate.successes)} contacts</small></div>
        </section>

        <section className="heatmap-section" aria-labelledby="heatmap-title">
          <div className="section-heading">
            <div>
              <p className="section-kicker">Goal map</p>
              <h2 id="heatmap-title">{mode === "final" ? "Where is the goalkeeper strongest?" : "Where does the teacher still lead?"}</h2>
              <p>{count(slice.sample_count)} paired on-target shots match the current filters.</p>
            </div>
            <div className="mode-tabs" role="tablist" aria-label="Heatmap metric">
              <button role="tab" aria-selected={mode === "final"} onClick={() => setMode("final")}>Final save rate</button>
              <button role="tab" aria-selected={mode === "teacher-gap"} onClick={() => setMode("teacher-gap")}>Gap from teacher</button>
            </div>
          </div>

          <div className="filters" aria-label="Heatmap filters">
            <fieldset>
              <legend>Location</legend>
              <div className="segmented-control">
                <button className={location === "intended_target" ? "active" : ""} onClick={() => setLocation("intended_target")}>Intended target</button>
                <button className={location === "unopposed_crossing" ? "active" : ""} onClick={() => setLocation("unopposed_crossing")}>Actual crossing</button>
              </div>
            </fieldset>
            <label>Shot style
              <select value={style} onChange={(event) => setStyle(event.target.value as StyleFilter)}>
                <option value="all">All styles</option><option value="placed">Placed</option><option value="power">Power</option><option value="curled">Curled</option>
              </select>
            </label>
            <label>Launch speed
              <select value={speed} onChange={(event) => setSpeed(event.target.value as SpeedFilter)}>
                <option value="all">All speeds</option><option value="slow">Slow</option><option value="medium">Medium</option><option value="fast">Fast</option>
              </select>
            </label>
          </div>

          <div className="goal-scroll" tabIndex={0} aria-label="Scrollable goal heatmap">
            <div className="goal-frame">
              <div className="crossbar-labels" aria-hidden="true"><span>Left</span><span>Centre left</span><span>Centre right</span><span>Right</span></div>
              <div className="heatmap-grid">
                {slice.cells.map((cell) => <HeatmapCell key={cell.cell_id} cell={cell} mode={mode} />)}
              </div>
            </div>
          </div>
          <div className={`legend ${mode}`}>
            <span>{mode === "final" ? "Lower save rate" : "Final ahead"}</span>
            <div className="legend-scale" aria-hidden="true">
              <i /><i /><i /><i /><i />
            </div>
            <span>{mode === "final" ? "Higher save rate" : "Teacher ahead"}</span>
            <p>{mode === "final" ? "Colour encodes final save rate; every cell also shows the exact value and Wilson 95% interval." : "Gap = teacher save rate − final save rate. Blue means the final goalkeeper leads; red means the teacher leads."}</p>
          </div>
        </section>

        <section className="report-section" aria-labelledby="overall-title">
          <div className="report-heading"><p className="section-kicker">Fixed benchmark</p><h2 id="overall-title">Overall performance</h2></div>
          <OverallTable data={data} />
        </section>

        <section className="report-section" aria-labelledby="breakdown-title">
          <div className="report-heading"><p className="section-kicker">Distribution checks</p><h2 id="breakdown-title">Performance by shot type</h2></div>
          <div className="breakdown-grid">
            {data.breakdown_tables.map((table) => <BreakdownTable key={table.dimension} table={table} policies={data.policies} />)}
          </div>
        </section>

        <section className="report-section split-report" aria-label="Balance and integrity">
          <div><div className="report-heading"><p className="section-kicker">Symmetry</p><h2>Left / right balance</h2></div><LeftRightTable data={data} /></div>
          <div><div className="report-heading"><p className="section-kicker">Technical health</p><h2>Evaluation integrity</h2></div><IntegrityTable data={data} /></div>
        </section>
      </main>

      <footer>
        <p>Same fixed shots for both policies · Expected-on-target population · Wilson 95% intervals</p>
        <p className="source-id">{data.source_benchmark_id} · digest {data.episode_key_digest.slice(0, 12)}</p>
      </footer>
    </>
  );
}

export default function App() {
  try {
    validateAnalysis(bundledAnalysis);
    return <AnalysisDashboard data={bundledAnalysis} />;
  } catch (reason) {
    const message = reason instanceof Error ? reason.message : "Invalid analysis artifact";
    return <main className="load-state"><strong>Analysis unavailable</strong><p>{message}</p></main>;
  }
}
