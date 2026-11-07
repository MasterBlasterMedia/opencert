import { post } from "./api";
function supported() {
  if (
    !window.PublicKeyCredential ||
    !PublicKeyCredential.parseCreationOptionsFromJSON
  )
    throw new Error(
      "Use an updated browser for passkeys, or choose email sign-in.",
    );
}
export async function registerPasskey(name: string) {
  supported();
  const options = await post<PublicKeyCredentialCreationOptionsJSON>(
    "/auth/passkeys/register/options",
  );
  const credential = (await navigator.credentials.create({
    publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options),
  })) as PublicKeyCredential | null;
  if (!credential)
    throw new Error("Passkey setup was cancelled. You can try again.");
  return post("/auth/passkeys/register", {
    credentialJson: JSON.stringify(credential.toJSON()),
    name,
  });
}
export async function signInPasskey() {
  supported();
  const options = await post<PublicKeyCredentialRequestOptionsJSON>(
    "/auth/passkeys/login/options",
  );
  const credential = (await navigator.credentials.get({
    publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options),
  })) as PublicKeyCredential | null;
  if (!credential)
    throw new Error("Passkey sign-in was cancelled. Try again or use email.");
  return post("/auth/passkeys/login", {
    credentialJson: JSON.stringify(credential.toJSON()),
    name: "",
  });
}
