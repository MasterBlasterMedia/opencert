import { useState } from "react";
import { useLocation, useNavigate, Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, post } from "./api";
import { useWorkspace, useSelectWorkspace } from "./App";
import { ErrorBox, PageTitle } from "./components";
import s from "./App.module.scss";
type Member = {
  userId: string;
  displayName: string;
  email: string;
  role: string;
};
const roles = ["Viewer", "Member", "Administrator"];
export function WorkspaceCreate() {
  const selectOrganization = useSelectWorkspace();
  const [name, setName] = useState("");
  const client = useQueryClient();
  const navigate = useNavigate();
  const create = useMutation({
    mutationFn: () => post<{ id: string }>("/organizations", { name }),
    onSuccess: async (data) => {
      selectOrganization(data.id);
      await client.invalidateQueries({ queryKey: ["me"] });
      navigate("/app");
    },
  });
  return (
    <section className={s.panel}>
      <h2>Create a workspace</h2>
      <p>Keep a separate set of websites and members together.</p>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate();
        }}
      >
        <label htmlFor="new-workspace">Workspace name</label>
        <input
          id="new-workspace"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
          maxLength={80}
        />
        <ErrorBox error={create.error} />
        <button className={s.button} disabled={create.isPending}>
          Create workspace
        </button>
      </form>
    </section>
  );
}
export function InvitationAccept() {
  const selectOrganization = useSelectWorkspace();
  const location = useLocation();
  const [invitation] = useState(
    () => new URLSearchParams(location.hash.slice(1)),
  );
  const client = useQueryClient();
  const navigate = useNavigate();
  const accept = useMutation({
    mutationFn: () =>
      post<{ id: string }>(`/invitations/${invitation.get("invite")}/accept`, {
        token: invitation.get("token"),
      }),
    onSuccess: async (data) => {
      selectOrganization(data.id);
      await client.invalidateQueries({ queryKey: ["me"] });
      navigate("/app/team", { replace: true });
    },
  });
  if (!invitation.get("invite") || !invitation.get("token") || accept.isSuccess)
    return null;
  return (
    <section className={s.panel}>
      <h2>You have a workspace invitation</h2>
      <p>
        Accept only if you expected this invitation. You must be signed in with
        the email address that received it.
      </p>
      <ErrorBox error={accept.error} />
      <button
        className={s.button}
        onClick={() => accept.mutate()}
        disabled={accept.isPending}
      >
        Accept invitation
      </button>
    </section>
  );
}
export function Team() {
  const { org } = useWorkspace();
  return <TeamContent key={org.id} />;
}
function TeamContent() {
  const { org, me } = useWorkspace();
  const client = useQueryClient();
  const navigate = useNavigate();
  const base = `/organizations/${org.id}`;
  const admin = ["Owner", "Administrator"].includes(org.role);
  const [email, setEmail] = useState("");
  const [role, setRole] = useState("Viewer");
  const [name, setName] = useState(org.name);
  const [confirmation, setConfirmation] = useState<{
    kind: string;
    id: string;
    label: string;
  } | null>(null);
  const [deleteName, setDeleteName] = useState("");
  const members = useQuery({
    queryKey: ["members", org.id],
    queryFn: () => api<Member[]>(base + "/members"),
  });
  const invites = useQuery({
    queryKey: ["invitations", org.id],
    queryFn: () =>
      api<{ id: string; email: string; role: string }[]>(base + "/invitations"),
    enabled: admin,
  });
  async function refresh() {
    await Promise.all([
      client.invalidateQueries({ queryKey: ["members"] }),
      client.invalidateQueries({ queryKey: ["invitations"] }),
      client.invalidateQueries({ queryKey: ["me"] }),
    ]);
  }
  const invite = useMutation({
    mutationFn: () => post(base + "/invitations", { email, role }),
    onSuccess: () => {
      setEmail("");
      refresh();
    },
  });
  const rename = useMutation({
    mutationFn: () =>
      api(base + "/profile", { method: "PUT", body: JSON.stringify({ name }) }),
    onSuccess: refresh,
  });
  const change = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: string }) =>
      api(base + "/members/" + userId + "/role", {
        method: "PUT",
        body: JSON.stringify({ role }),
      }),
    onSuccess: refresh,
  });
  const action = useMutation({
    mutationFn: () => {
      if (!confirmation) throw new Error("Choose an action.");
      if (confirmation.kind === "ownership")
        return post(base + "/ownership", { userId: confirmation.id });
      return api(base + "/" + confirmation.kind + "/" + confirmation.id, {
        method: "DELETE",
      });
    },
    onSuccess: () => {
      setConfirmation(null);
      refresh();
    },
  });
  const remove = useMutation({
    mutationFn: () =>
      api(base + "/", {
        method: "DELETE",
        body: JSON.stringify({ confirmation: deleteName }),
      }),
    onSuccess: async () => {
      await refresh();
      navigate("/app");
    },
  });
  return (
    <>
      <PageTitle title="Your workspace and team">
        Choose who can manage these websites. Sensitive changes require recent
        verification in <Link to="/app/account">Account settings</Link>.
      </PageTitle>
      <InvitationAccept />
      <ErrorBox
        error={members.error || invites.error || change.error || action.error}
      />
      <section className={s.panel}>
        <h2>{org.name}</h2>
        <p>Your role: {org.role}</p>
        {admin && (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              rename.mutate();
            }}
          >
            <label htmlFor="workspace-name">Workspace name</label>
            <input
              id="workspace-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={80}
              required
            />
            <ErrorBox error={rename.error} />
            <button className={s.secondary} disabled={rename.isPending}>
              Save workspace name
            </button>
          </form>
        )}
      </section>
      <section className={s.panel}>
        <h2>Members</h2>
        <p>
          Viewers can read. Members can manage websites and request
          certificates. Administrators also manage invitations. Owners control
          roles and ownership.
        </p>
        {members.data?.map((member) => (
          <div key={member.userId} className={s.statusRow}>
            <div>
              <strong>{member.displayName}</strong>
              <p>{member.email}</p>
            </div>
            <span>{member.role}</span>
            {org.role === "Owner" && member.role !== "Owner" && (
              <>
                <select
                  aria-label={`Role for ${member.email}`}
                  value={member.role}
                  onChange={(e) =>
                    change.mutate({
                      userId: member.userId,
                      role: e.target.value,
                    })
                  }
                  disabled={change.isPending}
                >
                  {roles.map((r) => (
                    <option key={r}>{r}</option>
                  ))}
                </select>
                <button
                  className={s.secondary}
                  onClick={() =>
                    setConfirmation({
                      kind: "ownership",
                      id: member.userId,
                      label: member.email,
                    })
                  }
                >
                  Transfer ownership
                </button>
              </>
            )}
            {member.role !== "Owner" &&
              (org.role === "Owner" ||
                (org.role === "Administrator" &&
                  member.role !== "Administrator") ||
                member.email === me.email) && (
                <button
                  className={s.secondary}
                  onClick={() =>
                    setConfirmation({
                      kind: "members",
                      id: member.userId,
                      label: member.email,
                    })
                  }
                >
                  Remove member
                </button>
              )}
          </div>
        ))}
      </section>
      {confirmation && (
        <section
          className={s.panel}
          role="group"
          aria-label="Confirm team change"
        >
          <h2>
            {confirmation.kind === "ownership"
              ? "Transfer workspace ownership?"
              : "Remove access?"}
          </h2>
          <p>
            {confirmation.kind === "ownership"
              ? `Make ${confirmation.label} the owner? You will become an administrator and lose control over ownership and role changes.`
              : `Remove access for ${confirmation.label}?`}
          </p>
          <button
            className={s.danger}
            onClick={() => action.mutate()}
            disabled={action.isPending}
          >
            Confirm change
          </button>
          <button className={s.secondary} onClick={() => setConfirmation(null)}>
            Cancel
          </button>
        </section>
      )}
      {admin && (
        <section className={s.panel}>
          <h2>Invite a teammate</h2>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              invite.mutate();
            }}
          >
            <label htmlFor="invite-email">Teammate’s email</label>
            <input
              id="invite-email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
            <label htmlFor="invite-role">Role</label>
            <select
              id="invite-role"
              value={role}
              onChange={(e) => setRole(e.target.value)}
            >
              {roles
                .filter((r) => r !== "Administrator" || org.role === "Owner")
                .map((r) => (
                  <option key={r}>{r}</option>
                ))}
            </select>
            <ErrorBox error={invite.error} />
            <button className={s.button} disabled={invite.isPending}>
              Send invitation
            </button>
            {invite.isSuccess && (
              <p role="status">Invitation queued for email delivery.</p>
            )}
          </form>
          {invites.data?.map((i) => (
            <div key={i.id} className={s.statusRow}>
              <span>
                {i.email} · {i.role}
              </span>
              <button
                className={s.secondary}
                onClick={() =>
                  setConfirmation({
                    kind: "invitations",
                    id: i.id,
                    label: i.email,
                  })
                }
              >
                Revoke invitation
              </button>
            </div>
          ))}
        </section>
      )}
      <WorkspaceCreate />
      {org.role === "Owner" && (
        <details className={s.panel}>
          <summary>Delete this workspace</summary>
          <p>
            Remove all other members first. Workspaces with certificate history
            must be retained. Deleting a workspace does not change DNS or your
            web server.
          </p>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              remove.mutate();
            }}
          >
            <label htmlFor="delete-workspace">Type {org.name} to confirm</label>
            <input
              id="delete-workspace"
              value={deleteName}
              onChange={(e) => setDeleteName(e.target.value)}
              required
            />
            <ErrorBox error={remove.error} />
            <button
              className={s.danger}
              disabled={remove.isPending || deleteName !== org.name}
            >
              Delete workspace
            </button>
          </form>
        </details>
      )}
    </>
  );
}
