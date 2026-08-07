import { fireEvent, render, screen, within } from "@testing-library/react";
import artifact from "../public/data/goalkeeper-analysis-v1.json";
import { AnalysisDashboard } from "./App";
import { findSlice, validateAnalysis } from "./analysis";
import type { AnalysisData } from "./types";

const data = artifact as AnalysisData;

describe("Stage 8 analysis dashboard", () => {
  it("accepts the frozen analysis artifact", () => {
    expect(() => validateAnalysis(data)).not.toThrow();
    expect(data.filter_slices).toHaveLength(32);
  });

  it("renders one readable 4 x 3 heatmap and the requested statistics", () => {
    render(<AnalysisDashboard data={data} />);
    expect(screen.getAllByTestId("heatmap-cell")).toHaveLength(12);
    expect(screen.getByRole("heading", { name: "Where is the goalkeeper strongest?" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Overall performance" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Performance by shot type" })).toBeTruthy();
    expect(screen.queryByText(/stand.centre/i)).toBeNull();
  });

  it("switches to the teacher gap without changing the paired slice", () => {
    render(<AnalysisDashboard data={data} />);
    fireEvent.click(screen.getByRole("tab", { name: "Gap from teacher" }));
    expect(screen.getByRole("heading", { name: "Where does the teacher still lead?" })).toBeTruthy();
    for (const cell of screen.getAllByTestId("heatmap-cell")) {
      expect(within(cell).getByText(/pp$/)).toBeTruthy();
    }
  });

  it("filters by crossing, style, and speed using precomputed data", () => {
    render(<AnalysisDashboard data={data} />);
    fireEvent.click(screen.getByRole("button", { name: "Actual crossing" }));
    fireEvent.change(screen.getByLabelText("Shot style"), { target: { value: "curled" } });
    fireEvent.change(screen.getByLabelText("Launch speed"), { target: { value: "fast" } });

    const expected = findSlice(data, "unopposed_crossing", "curled", "fast");
    const heading = screen.getByRole("heading", { name: "Where is the goalkeeper strongest?" }).parentElement!;
    expect(within(heading).getByText(new RegExp(`${expected.sample_count.toLocaleString()} paired`))).toBeTruthy();
  });
});
