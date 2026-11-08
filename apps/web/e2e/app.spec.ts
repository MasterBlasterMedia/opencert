import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
const mailDirectory = path.resolve(
  process.env.OPENCERT_TEST_MAIL ?? "../../.local/mail",
);
test("public pages, mobile layout and accessibility", async ({ page }) => {
  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "A safer website. A simpler way." }),
  ).toBeVisible();
  const results = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
    .analyze();
  expect(results.violations).toEqual([]);
  await page.screenshot({
    path: "test-results/home-desktop.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(
    page.getByRole("link", { name: "Protect my website", exact: true }).first(),
  ).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBeTruthy();
  await page.screenshot({
    path: "test-results/home-mobile.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: "Use dark theme" }).click();
  expect(
    (await new AxeBuilder({ page }).withTags(["wcag2aa"]).analyze()).violations,
  ).toEqual([]);
  await page.goto("/help/godaddy");
  await expect(
    page.getByRole("heading", { name: "A GoDaddy domain, hosted anywhere" }),
  ).toBeVisible();
});
test("email account, onboarding and persistent website details", async ({
  page,
}) => {
  const email = `browser-${Date.now()}@example.com`;
  await page.goto("/login");
  await page.getByLabel("Email address").fill(email);
  await page.getByRole("button", { name: "Continue with email" }).click();
  await expect(
    page.getByRole("heading", { name: "Check your inbox." }),
  ).toBeVisible();
  let code = "";
  await expect
    .poll(async () => {
      for (const file of await readdir(mailDirectory)) {
        const text = await readFile(path.join(mailDirectory, file), "utf8");
        if (text.includes(email)) code = text.match(/code is (\d+)/)?.[1] ?? "";
      }
      return code;
    })
    .not.toBe("");
  await page.getByLabel("Sign-in code").fill(code);
  await page.getByRole("button", { name: "Confirm sign in" }).click();
  await expect(
    page.getByRole("heading", { name: "Welcome to OpenCert." }),
  ).toBeVisible();
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  await page.screenshot({
    path: "test-results/welcome-desktop.png",
    fullPage: true,
  });
  await page
    .getByRole("link", { name: "Protect my website", exact: true })
    .click();
  await page
    .getByLabel("Website address", { exact: true })
    .fill("https://example.com/");
  await page.getByRole("button", { name: "Find my next step" }).click();
  await expect(
    page.getByRole("heading", { name: "example.com", exact: true }),
  ).toBeVisible();
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "example.com", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText("Automatic renewal", { exact: true }),
  ).toBeVisible();
  await page.screenshot({
    path: "test-results/website-desktop.png",
    fullPage: true,
  });
  await page.goto("/app/team");
  await expect(
    page.getByRole("heading", { name: "Your workspace and team" }),
  ).toBeVisible();
  await page
    .getByLabel("Workspace name", { exact: true })
    .last()
    .fill("Second workspace");
  await page
    .getByRole("button", { name: "Create workspace", exact: true })
    .click();
  await expect(page.getByLabel("YOUR WORKSPACE")).toHaveText(
    /Second workspace/,
  );
  await expect(
    page.getByRole("heading", { name: "Welcome to OpenCert." }),
  ).toBeVisible();
  await page.goto("/app/team");
  await expect(
    page.getByLabel("Workspace name", { exact: true }).first(),
  ).toHaveValue("Second workspace");
  await page.goto("/app/account");
  await page.getByLabel("Display name").fill("Browser tester");
  await page.getByRole("button", { name: "Save profile" }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "Profile saved." }),
  ).toBeVisible();
  await page.reload();
  await expect(page.getByLabel("Display name")).toHaveValue("Browser tester");
  const alerts = page.getByRole("checkbox", {
    name: "Email me about certificate expiration, renewal reminders and failed requests",
  });
  await expect(alerts).toBeChecked();
  await alerts.uncheck();
  await expect(
    page
      .getByRole("status")
      .filter({ hasText: "Notification preference saved." }),
  ).toBeVisible();
  await page.reload();
  await expect(alerts).not.toBeChecked();
});
test("passkey registration, verified sign-in, replay rejection and removal", async ({
  page,
  context,
}) => {
  const cdp = await context.newCDPSession(page);
  await cdp.send("WebAuthn.enable");
  const { authenticatorId } = await cdp.send(
    "WebAuthn.addVirtualAuthenticator",
    {
      options: {
        protocol: "ctap2",
        transport: "internal",
        hasResidentKey: true,
        hasUserVerification: true,
        isUserVerified: true,
        automaticPresenceSimulation: true,
      },
    },
  );
  const email = `passkey-${Date.now()}@example.com`;
  await page.goto("/login");
  await page.getByLabel("Email address").fill(email);
  await page.getByRole("button", { name: "Continue with email" }).click();
  await expect(
    page.getByRole("heading", { name: "Check your inbox." }),
  ).toBeVisible();
  let code = "";
  await expect
    .poll(async () => {
      for (const file of await readdir(mailDirectory)) {
        const text = await readFile(path.join(mailDirectory, file), "utf8");
        if (text.includes(email)) code = text.match(/code is (\d+)/)?.[1] ?? "";
      }
      return code;
    })
    .not.toBe("");
  await page.getByLabel("Sign-in code").fill(code);
  await page.getByRole("button", { name: "Confirm sign in" }).click();
  await expect(
    page.getByRole("heading", { name: "Welcome to OpenCert." }),
  ).toBeVisible();
  await page.goto("/app/account");
  await page.getByLabel("Name this passkey").fill("Browser test key");
  await page
    .getByRole("button", { name: "Add a passkey", exact: true })
    .click();
  await expect(
    page.getByText("Browser test key", { exact: true }),
  ).toBeVisible();
  const credentials = await cdp.send("WebAuthn.getCredentials", {
    authenticatorId,
  });
  expect(credentials.credentials).toHaveLength(1);
  const csrf = await (await page.request.get("/api/v1/auth/csrf")).json();
  expect(
    (
      await page.request.post("/api/v1/auth/logout", {
        headers: { "X-CSRF-Token": csrf.token },
        data: {},
      })
    ).status(),
  ).toBe(204);
  await page.goto("/login");
  const loginRequest = page.waitForRequest((r) =>
    r.url().endsWith("/auth/passkeys/login"),
  );
  await page
    .getByRole("button", { name: "Sign in with a passkey", exact: true })
    .click();
  const assertion = (await loginRequest).postDataJSON();
  await expect(
    page.getByRole("heading", { name: "Welcome to OpenCert." }),
  ).toBeVisible();
  const freshCsrf = await (await page.request.get("/api/v1/auth/csrf")).json();
  expect(
    (
      await page.request.post("/api/v1/auth/passkeys/login", {
        headers: { "X-CSRF-Token": freshCsrf.token },
        data: assertion,
      })
    ).status(),
  ).toBe(400);
  await page.goto("/app/account");
  await expect(
    page.getByText("Browser test key", { exact: true }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Remove passkey", exact: true })
    .click();
  await expect(page.getByText("Browser test key", { exact: true })).toHaveCount(
    0,
  );
  await cdp.send("WebAuthn.removeVirtualAuthenticator", { authenticatorId });
});
