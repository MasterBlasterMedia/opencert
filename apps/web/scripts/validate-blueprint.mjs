import fs from "node:fs/promises";
import Ajv2020 from "ajv/dist/2020.js";
import YAML from "yaml";
const path = process.argv[2] ?? "../../render.yaml";
const schemaFile = process.argv[3];
const schema = schemaFile
  ? JSON.parse(await fs.readFile(schemaFile, "utf8"))
  : await fetch("https://render.com/schema/render.yaml.json").then((r) => {
      if (!r.ok) throw new Error("Render schema unavailable");
      return r.json();
    });
const validate = new Ajv2020({ strict: false, validateFormats: false }).compile(
  schema,
);
if (!validate(YAML.parse(await fs.readFile(path, "utf8")))) {
  console.error(validate.errors);
  process.exitCode = 1;
} else console.log("Render Blueprint schema validation passed.");
