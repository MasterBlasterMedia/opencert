import { render, screen, cleanup } from "@testing-library/react";
import { BrowserRouter } from "react-router-dom";
import { afterEach, describe, expect, it } from "vitest";
import { Status, WebsiteCard, ErrorBox } from "./components";
import type { Website } from "./api";
afterEach(cleanup);
describe("truthful website feedback", () => {
  it("names an unverified state rather than implying protection", () => {
    render(<Status value="Unable to Verify" />);
    expect(screen.getByText("Unable to Verify")).toBeVisible();
    expect(screen.queryByText("Protected")).not.toBeInTheDocument();
  });
  it("shows unconfirmed renewal even when HTTPS is protected", () => {
    const site = {
      id: "a",
      hostname: "example.com",
      status: "Protected",
      renewal: "Not confirmed",
      hostingProvider: null,
      checkedAt: null,
    } as Website;
    render(
      <BrowserRouter>
        <WebsiteCard site={site} />
      </BrowserRouter>,
    );
    expect(screen.getByText("Renewal: not confirmed")).toBeVisible();
  });
  it("exposes actionable errors as alerts", () => {
    render(<ErrorBox error={new Error("Refresh and try again.")} />);
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Refresh and try again.",
    );
  });
});
