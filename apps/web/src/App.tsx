import { createContext, useContext, useEffect, useState } from "react";
import {
  Link,
  NavLink,
  Navigate,
  Outlet,
  Route,
  Routes,
  useLocation,
  useNavigate,
} from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ShieldCheck,
  ArrowRight,
  Globe2,
  House,
  Plus,
  Bell,
  CircleHelp,
  Settings,
  Code2,
  Sun,
  Moon,
  Menu,
  LogOut,
  Check,
  LockKeyhole,
  ArrowUpRight,
  BookOpen,
} from "lucide-react";
import {
  api,
  ApiError,
  post,
  type Me,
  type Organization,
  type Website,
} from "./api";
import { ErrorBox, Loading, PageTitle, WebsiteCard } from "./components";
import { articles, guides } from "./content";
import {
  Login,
  Protect,
  WebsiteDetail,
  Account,
  Notifications,
  Advanced,
} from "./pages";
import s from "./App.module.scss";
import { Team, WorkspaceCreate, InvitationAccept } from "./Team";
const WorkspaceContext = createContext<{
  me: Me;
  org: Organization | null;
  selectOrganization: (id: string) => void;
} | null>(null);
export function useWorkspace() {
  const context = useContext(WorkspaceContext);
  if (!context?.org) throw new Error("Workspace unavailable");
  return { ...context, org: context.org };
}
export function useSelectWorkspace() {
  const context = useContext(WorkspaceContext);
  if (!context) throw new Error("Sign in first");
  return context.selectOrganization;
}
export function useAccount() {
  const context = useContext(WorkspaceContext);
  if (!context) throw new Error("Sign in first");
  return context.me;
}
export function useWebsites() {
  const { org } = useWorkspace();
  return useQuery({
    queryKey: ["websites", org.id],
    queryFn: () => api<Website[]>(`/organizations/${org.id}/websites`),
    refetchInterval: 15_000,
  });
}
export function Logo() {
  return (
    <Link to="/" className={s.logo} aria-label="OpenCert home">
      <span>
        <ShieldCheck size={23} strokeWidth={1.8} />
      </span>
      OpenCert<span className={s.logoDot}>.</span>
    </Link>
  );
}
function ThemeToggle() {
  const [dark, setDark] = useState(
    () => localStorage.getItem("theme") === "dark",
  );
  useEffect(() => {
    document.documentElement.dataset.theme = dark ? "dark" : "light";
    localStorage.setItem("theme", dark ? "dark" : "light");
  }, [dark]);
  return (
    <button
      className={s.iconButton}
      aria-label={dark ? "Use light theme" : "Use dark theme"}
      onClick={() => setDark(!dark)}
    >
      {dark ? <Sun size={19} /> : <Moon size={19} />}
    </button>
  );
}
function ScrollReset() {
  const { pathname } = useLocation();
  useEffect(() => {
    window.scrollTo(0, 0);
    document.title = "OpenCert — Free website security, made simple";
  }, [pathname]);
  return null;
}
function PublicLayout() {
  const [menu, setMenu] = useState(false);
  return (
    <>
      <a href="#main" className={s.skip}>
        Skip to content
      </a>
      <header className={s.publicHeader}>
        <Logo />
        <button
          className={s.mobileMenu}
          aria-label="Toggle navigation"
          aria-expanded={menu}
          onClick={() => setMenu(!menu)}
        >
          <Menu />
        </button>
        <nav
          aria-label="Main navigation"
          className={menu ? s.publicNavOpen : s.publicNav}
        >
          <Link to="/how-it-works">How it works</Link>
          <Link to="/features">Features</Link>
          <Link to="/help">Help center</Link>
          <Link to="/open-source">
            Open source <ArrowUpRight size={13} />
          </Link>
        </nav>
        <div className={s.headerActions}>
          <ThemeToggle />
          <Link to="/login" className={s.signIn}>
            Sign in
          </Link>
          <Link to="/app/protect" className={s.button}>
            Get started <ArrowRight size={16} />
          </Link>
        </div>
      </header>
      <main id="main">
        <Outlet />
      </main>
      <footer className={s.footer}>
        <div>
          <Logo />
          <p>Free website security, made simple.</p>
          <small>Free software. No subscriptions. No ads.</small>
        </div>
        <div>
          <h3>Explore</h3>
          <Link to="/features">Features</Link>
          <Link to="/providers">Supported hosts</Link>
          <Link to="/faq">FAQ</Link>
          <Link to="/status">Service status</Link>
        </div>
        <div>
          <h3>Resources</h3>
          <Link to="/docs">Documentation</Link>
          <Link to="/help">Help center</Link>
          <Link to="/contributing">Contributing</Link>
          <Link to="/contact">Contact</Link>
        </div>
        <div>
          <h3>Trust</h3>
          <Link to="/security">Security</Link>
          <Link to="/privacy">Privacy policy</Link>
          <Link to="/terms">Terms of service</Link>
          <Link to="/security-disclosure">Report a vulnerability</Link>
        </div>
        <p className={s.footerNote}>
          OpenCert manages certificates from established certificate
          authorities. Domain registration and hosting costs are separate.
        </p>
      </footer>
    </>
  );
}
function Home() {
  return (
    <>
      <section className={s.hero}>
        <div className={s.heroCopy}>
          <span className={s.pill}>
            <span /> FREE & OPEN SOURCE
          </span>
          <h1>
            A safer website.
            <br />
            <em>A simpler way.</em>
          </h1>
          <p>
            Free SSL certificates. No confusion. No yearly certificate fees.
            Understand your website’s security and take the right next step.
          </p>
          <div className={s.heroButtons}>
            <Link to="/app/protect" className={s.button}>
              Protect my website <ArrowRight size={18} />
            </Link>
            <Link to="/how-it-works" className={s.textLink}>
              See how it works <ArrowUpRight size={16} />
            </Link>
          </div>
          <div className={s.heroChecks}>
            <span>
              <Check size={15} /> No credit card
            </span>
            <span>
              <Check size={15} /> Your keys stay yours
            </span>
          </div>
        </div>
        <div
          className={s.heroVisual}
          aria-label="OpenCert helps you check HTTPS, choose a setup guide, and understand renewal"
        >
          <div className={s.orbit} />
          <div className={s.heroShield}>
            <ShieldCheck size={70} strokeWidth={1.3} />
          </div>
          <div className={s.visualCard}>
            <span className={s.eyebrow}>A CLEARER PATH TO HTTPS</span>
            <h2>
              Less guesswork.
              <br />
              More confidence.
            </h2>
            <div>
              <span className={s.stepCircle}>1</span>Understand your current
              setup
            </div>
            <div>
              <span className={s.stepCircle}>2</span>Choose the right protection
            </div>
            <div>
              <span className={s.stepCircle}>3</span>Check your live website
            </div>
          </div>
          <span className={s.visualLabel}>
            <LockKeyhole size={15} /> Built around your privacy
          </span>
        </div>
      </section>
      <div className={s.valueStrip}>
        <span>
          GOOD FOR YOUR WEBSITE.
          <br />
          <strong>Even better for your peace of mind.</strong>
        </span>
        <span>
          <Globe2 /> Trusted certificate authorities
        </span>
        <span>
          <Code2 /> Open-source software
        </span>
        <span>
          <CircleHelp /> Guidance at every step
        </span>
      </div>
      <section className={s.section}>
        <div className={s.sectionHeading}>
          <div>
            <p className={s.eyebrow}>SECURITY, WITHOUT THE JARGON</p>
            <h2>
              You bring the website.
              <br />
              We’ll help with the next step.
            </h2>
          </div>
          <p>
            Your host may already offer free HTTPS. We help you find the
            simplest supported setup before requesting a separate certificate.
          </p>
        </div>
        <div className={s.threeCards}>
          {[
            [
              Globe2,
              "Tell us your website",
              "Start with the address your visitors use. No complicated settings to figure out first.",
            ],
            [
              BookOpen,
              "Follow your own guide",
              "Get a clear path based on your hosting setup, with help when you need it.",
            ],
            [
              ShieldCheck,
              "Know where you stand",
              "See what has been verified, what needs attention, and who handles renewal.",
            ],
          ].map(([Icon, title, text], i) => {
            const I = Icon as typeof Globe2;
            return (
              <article className={s.featureCard} key={i}>
                <div>
                  <I size={24} />
                  <span>0{i + 1}</span>
                </div>
                <h3>{String(title)}</h3>
                <p>{String(text)}</p>
              </article>
            );
          })}
        </div>
      </section>
      <section className={s.hostBanner}>
        <div>
          <p className={s.eyebrow}>ALREADY INCLUDED? EVEN BETTER.</p>
          <h2>Your host might have you covered.</h2>
          <p>
            Bought your domain at GoDaddy, but host somewhere else? That’s
            normal. We’ll help you tell the difference.
          </p>
          <Link className={s.textLink} to="/providers">
            Find your hosting guide <ArrowRight size={17} />
          </Link>
        </div>
        <div className={s.hostTags}>
          {[
            "Render",
            "Vercel",
            "Netlify",
            "Cloudflare",
            "cPanel",
            "Your own server",
          ].map((p) => (
            <span key={p}>{p}</span>
          ))}
        </div>
      </section>
      <section className={s.finalCta}>
        <ShieldCheck size={34} />
        <h2>One less thing to wonder about.</h2>
        <p>Let’s find the right HTTPS setup for your website.</p>
        <Link to="/app/protect" className={s.button}>
          Protect my website <ArrowRight size={18} />
        </Link>
      </section>
    </>
  );
}
function Article({ slug }: { slug: string }) {
  const a = articles[slug];
  return (
    <section className={s.article}>
      <p className={s.eyebrow}>THE OPENCERT GUIDE</p>
      <h1>{a.title}</h1>
      <p className={s.lead}>{a.intro}</p>
      {a.sections.map(([title, text]) => (
        <section key={title}>
          <h2>{title}</h2>
          <p>{text}</p>
        </section>
      ))}
      <Link to="/help" className={s.textLink}>
        Explore the help center <ArrowRight size={16} />
      </Link>
    </section>
  );
}
export function Help() {
  const [filter, setFilter] = useState("");
  return (
    <div className={s.section}>
      <PageTitle
        eyebrow="A LITTLE GUIDANCE GOES A LONG WAY"
        title="How can we help?"
      >
        Straightforward answers, wherever your website lives.
      </PageTitle>
      <label className={s.search}>
        Find your hosting provider
        <input
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          placeholder="Try GoDaddy, Render or WordPress…"
          type="search"
        />
      </label>
      <div className={s.guideGrid}>
        {Object.entries(guides)
          .filter(([key, guide]) =>
            `${key} ${guide.title}`
              .toLowerCase()
              .includes(filter.toLowerCase()),
          )
          .map(([key, guide]) => (
            <Link key={key} to={`/help/${key}`} className={s.guideCard}>
              <BookOpen size={22} />
              <h2>{guide.title}</h2>
              <p>{guide.intro}</p>
              <span>
                Read the guide <ArrowRight size={16} />
              </span>
            </Link>
          ))}
      </div>
      <p>
        Not sure where to start?{" "}
        <Link to="/faq">Read the common questions</Link>.
      </p>
    </div>
  );
}
function Guide({ slug }: { slug: string }) {
  const g = guides[slug];
  return (
    <article className={s.article}>
      <Link to="/help" className={s.textLink}>
        ← Help center
      </Link>
      <h1>{g.title}</h1>
      <p className={s.lead}>{g.intro}</p>
      <ol className={s.guideSteps}>
        {g.steps.map((step) => (
          <li key={step}>{step}</li>
        ))}
      </ol>
      {g.link && (
        <a href={g.link} target="_blank" rel="noreferrer">
          {g.linkLabel} ↗
        </a>
      )}
      <div className={s.note}>
        After setup, use OpenCert to check the public website. A saved DNS
        record or issued certificate is not proof that HTTPS is working.
      </div>
    </article>
  );
}
function ServiceStatus() {
  const q = useQuery({
    queryKey: ["service-status"],
    queryFn: () => api<Record<string, string>>("/status"),
  });
  return (
    <section className={s.article}>
      <PageTitle title="Service status">
        This describes OpenCert’s services, separately from your websites.
      </PageTitle>
      {q.isLoading && <Loading />}
      <ErrorBox error={q.error} />
      {q.data &&
        ["api", "database", "worker"].map((key) => (
          <div className={s.statusRow} key={key}>
            <strong>{key.toUpperCase()}</strong>
            <span>{q.data[key]}</span>
          </div>
        ))}
      <button
        onClick={() => q.refetch()}
        className={s.secondary}
        disabled={q.isFetching}
      >
        Check again
      </button>
    </section>
  );
}
function WorkspaceLayout() {
  const location = useLocation();
  const q = useQuery({ queryKey: ["me"], queryFn: () => api<Me>("/auth/me") });
  const [orgId, setOrgId] = useState(
    () => localStorage.getItem("organization") ?? "",
  );
  const [menu, setMenu] = useState(false);
  const [advanced, setAdvanced] = useState(
    () => localStorage.getItem("advanced") === "true",
  );
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  function selectOrganization(id: string) {
    setOrgId(id);
    localStorage.setItem("organization", id);
  }
  const logout = useMutation({
    mutationFn: () => post("/auth/logout"),
    onSuccess: () => {
      queryClient.clear();
      navigate("/login");
    },
  });
  if (q.isLoading) return <Loading />;
  if (q.error instanceof ApiError && q.error.status === 401)
    return (
      <Navigate
        to="/login"
        state={{
          invitation:
            location.pathname === "/app/team" ? location.hash : undefined,
        }}
        replace
      />
    );
  if (q.error || !q.data)
    return (
      <div className={s.article}>
        <ErrorBox error={q.error} />
        <button onClick={() => q.refetch()}>Try again</button>
      </div>
    );
  const org =
    q.data.organizations.find((o) => o.id === orgId) ?? q.data.organizations[0];
  if (!org)
    return (
      <WorkspaceContext.Provider
        value={{ me: q.data, org: null, selectOrganization }}
      >
        <div className={s.article}>
          <Logo />
          {location.pathname === "/app/account" ? (
            <Account />
          ) : (
            <>
              <PageTitle title="Create your next workspace">
                Your account is active. Create a workspace or accept an
                invitation to manage websites.
              </PageTitle>
              <InvitationAccept />
              <WorkspaceCreate />
              <Link to="/app/account">Account settings</Link>
            </>
          )}
        </div>
      </WorkspaceContext.Provider>
    );
  const nav = [
    [House, "/app", "Home"],
    [Globe2, "/app/websites", "My websites"],
    [Plus, "/app/protect", "Protect a website"],
    [Bell, "/app/notifications", "Notifications"],
    [CircleHelp, "/app/help", "Help center"],
  ] as const;
  return (
    <WorkspaceContext.Provider value={{ me: q.data, org, selectOrganization }}>
      <a href="#main" className={s.skip}>
        Skip to content
      </a>
      <div className={s.workspace}>
        <aside className={`${s.sidebar} ${menu ? s.sidebarOpen : ""}`}>
          <Logo />
          <div className={s.workspacePicker}>
            <label htmlFor="workspace">YOUR WORKSPACE</label>
            <select
              id="workspace"
              value={org.id}
              onChange={(e) => {
                setOrgId(e.target.value);
                localStorage.setItem("organization", e.target.value);
              }}
            >
              {q.data.organizations.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.name}
                </option>
              ))}
            </select>
          </div>
          <nav aria-label="Workspace navigation">
            {nav.map(([Icon, href, label]) => (
              <NavLink
                key={href}
                to={href}
                end={href === "/app"}
                onClick={() => setMenu(false)}
                className={({ isActive }) => (isActive ? s.activeNav : "")}
              >
                <Icon size={19} />
                {label}
              </NavLink>
            ))}
            <div className={s.navDivider} />
            <NavLink to="/app/team">
              <Globe2 size={19} />
              Workspace & team
            </NavLink>
            <NavLink to="/app/account">
              <Settings size={19} />
              Account settings
            </NavLink>
            <button
              className={s.advancedToggle}
              onClick={() => {
                setAdvanced(!advanced);
                localStorage.setItem("advanced", String(!advanced));
              }}
              aria-pressed={advanced}
            >
              <Code2 size={19} />
              Advanced tools<span>{advanced ? "On" : "Off"}</span>
            </button>
            {advanced && (
              <NavLink to="/app/advanced">Certificates & activity</NavLink>
            )}
          </nav>
          <div className={s.sidebarBottom}>
            <div className={s.localMark}>
              <ShieldCheck size={18} />
              <div>
                <strong>Free. Open. Yours.</strong>
                <small>Built for website owners.</small>
              </div>
            </div>
            <Link to="/security">Security & release status ↗</Link>
          </div>
        </aside>
        <div className={s.workspaceContent}>
          <header className={s.appHeader}>
            <button
              className={s.mobileMenu}
              onClick={() => setMenu(!menu)}
              aria-expanded={menu}
              aria-label="Toggle navigation"
            >
              <Menu />
            </button>
            <span>YOUR WEBSITE, IN GOOD HANDS</span>
            <div>
              <ThemeToggle />
              <Link
                to="/app/account"
                className={s.avatar}
                aria-label="Account settings"
              >
                {q.data.email[0].toUpperCase()}
              </Link>
              <button
                className={s.iconButton}
                aria-label="Sign out"
                onClick={() => logout.mutate()}
                disabled={logout.isPending}
              >
                <LogOut size={18} />
              </button>
            </div>
          </header>
          <main id="main" className={s.appMain}>
            <ErrorBox error={logout.error} />
            <Outlet />
          </main>
          <div className={s.appFooter}>
            OpenCert <span>Free website security, made simple.</span>
            <Link to="/status">Service status ↗</Link>
          </div>
        </div>
      </div>
    </WorkspaceContext.Provider>
  );
}
function Dashboard({ listOnly = false }: { listOnly?: boolean }) {
  const q = useWebsites();
  const [filter, setFilter] = useState("");
  if (q.isLoading) return <Loading />;
  const websites = q.data ?? [];
  return (
    <>
      <PageTitle
        eyebrow="A LITTLE CLARITY. A LOT LESS WORRY."
        title={
          listOnly
            ? "My websites"
            : websites.length
              ? "Your website security, at a glance."
              : "Welcome to OpenCert."
        }
        action={
          websites.length > 0 && (
            <Link to="/app/protect" className={s.button}>
              <Plus size={17} />
              Protect a website
            </Link>
          )
        }
      >
        {websites.length
          ? "Understand what’s working and what needs your attention."
          : "Let’s protect your website. We’ll guide you through it."}
      </PageTitle>
      <ErrorBox error={q.error} />
      {q.error && <button onClick={() => q.refetch()}>Try again</button>}
      {!q.error && !websites.length ? (
        <div className={s.welcome}>
          <div className={s.welcomeIcon}>
            <ShieldCheck size={50} strokeWidth={1.4} />
          </div>
          <p className={s.eyebrow}>YOUR FIRST STEP STARTS HERE</p>
          <h2>
            Big peace of mind.
            <br />
            One website address.
          </h2>
          <p>
            You don’t need to know about certificates or DNS. Tell us your
            website, and we’ll help you find the best way to protect it.
          </p>
          <Link to="/app/protect" className={s.button}>
            Protect my website <ArrowRight size={18} />
          </Link>
          <small>No certificate fees. No technical experience needed.</small>
        </div>
      ) : (
        <>
          {!listOnly && (
            <div className={s.metrics}>
              <div>
                <Globe2 size={19} />
                <span>Total websites</span>
                <strong>{websites.length}</strong>
              </div>
              <div>
                <ShieldCheck size={19} />
                <span>Protected</span>
                <strong>
                  {websites.filter((w) => w.status === "Protected").length}
                </strong>
              </div>
              <div>
                <Bell size={19} />
                <span>Need attention</span>
                <strong>
                  {websites.filter((w) => w.status !== "Protected").length}
                </strong>
              </div>
            </div>
          )}
          <div className={s.listHeading}>
            <h2>Your websites</h2>
            <label>
              Filter websites
              <input
                type="search"
                value={filter}
                onChange={(e) => setFilter(e.target.value)}
                placeholder="Search by website name"
              />
            </label>
          </div>
          <div className={s.websiteList}>
            {websites
              .filter((w) => w.hostname.includes(filter.toLowerCase()))
              .map((site) => (
                <WebsiteCard key={site.id} site={site} />
              ))}
          </div>
          <p className={s.subtle}>
            Showing up to 50 websites. Protected means the latest HTTPS check
            succeeded; renewal is assessed separately.
          </p>
        </>
      )}
      <div className={s.helpBanner}>
        <CircleHelp size={25} />
        <div>
          <h3>Already paying for hosting?</h3>
          <p>
            Your host may include free HTTPS. We’ll help you check before you
            request a certificate.
          </p>
        </div>
        <Link to="/help">
          Find my guide <ArrowRight size={16} />
        </Link>
      </div>
    </>
  );
}
export default function App() {
  return (
    <>
      <ScrollReset />
      <Routes>
        <Route element={<PublicLayout />}>
          <Route index element={<Home />} />
          <Route path="login" element={<Login />} />
          <Route path="get-free-ssl" element={<Navigate to="/app/protect" />} />
          {Object.keys(articles).map((slug) => (
            <Route key={slug} path={slug} element={<Article slug={slug} />} />
          ))}
          {["help", "docs", "providers"].map((slug) => (
            <Route key={slug} path={slug} element={<Help />} />
          ))}
          {Object.keys(guides).map((slug) => (
            <Route
              key={slug}
              path={`help/${slug}`}
              element={<Guide slug={slug} />}
            />
          ))}
          <Route path="status" element={<ServiceStatus />} />
        </Route>
        <Route path="app" element={<WorkspaceLayout />}>
          <Route index element={<Dashboard />} />
          <Route path="websites" element={<Dashboard listOnly />} />
          <Route path="protect" element={<Protect />} />
          <Route path="websites/:id" element={<WebsiteDetail />} />
          <Route path="account" element={<Account />} />
          <Route path="team" element={<Team />} />
          <Route path="notifications" element={<Notifications />} />
          <Route path="advanced" element={<Advanced />} />
          <Route path="help" element={<Help />} />
        </Route>
        <Route
          path="*"
          element={
            <div className={s.article}>
              <h1>That page wasn’t found.</h1>
              <Link to="/">Go to OpenCert home</Link>
            </div>
          }
        />
      </Routes>
    </>
  );
}
