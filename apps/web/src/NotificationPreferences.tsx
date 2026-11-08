import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api } from "./api";
import { ErrorBox } from "./components";
import s from "./App.module.scss";

type Preferences = { certificateEmails: boolean };
export function NotificationPreferences() {
  const client = useQueryClient();
  const [pendingValue, setPendingValue] = useState<boolean | null>(null);
  const preferences = useQuery({
    queryKey: ["notification-preferences"],
    queryFn: () => api<Preferences>("/account/notifications"),
  });
  const save = useMutation({
    mutationFn: (certificateEmails: boolean) =>
      api<Preferences>("/account/notifications", {
        method: "PUT",
        body: JSON.stringify({ certificateEmails }),
      }),
    onSuccess: (data) =>
      client.setQueryData(["notification-preferences"], data),
    onSettled: () => setPendingValue(null),
  });
  return (
    <section className={s.panel}>
      <h2>Email notifications</h2>
      <p>
        Website checks run every six hours. Certificate reminders and failed
        requests also appear in your workspace notifications. Renewal and
        installation still need your action.
      </p>
      {preferences.data && (
        <label className={s.checkbox}>
          <input
            type="checkbox"
            checked={pendingValue ?? preferences.data.certificateEmails}
            disabled={save.isPending}
            onChange={(event) => {
              setPendingValue(event.target.checked);
              save.mutate(event.target.checked);
            }}
          />
          Email me about certificate expiration, renewal reminders and failed
          requests
        </label>
      )}
      {preferences.isPending && <p role="status">Loading your preferences…</p>}
      <p>
        Sign-in, account changes and other security messages are always sent.
      </p>
      <ErrorBox error={preferences.error || save.error} />
      {save.isSuccess && <p role="status">Notification preference saved.</p>}
    </section>
  );
}
