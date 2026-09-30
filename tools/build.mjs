// ═══════════ build.mjs — сборка в один HTML ═══════════
// Никаких внешних сборщиков: модули движка склеиваются в одну функцию-обёртку,
// из них вырезаются import/export, наружу отдаётся объект Engine.
// Запуск: npm run build  →  dist/tracker.html
import fs from "node:fs";

const ROOT = new URL("../", import.meta.url);
const read = p => fs.readFileSync(new URL(p, ROOT), "utf8");
const pkg = JSON.parse(read("package.json"));

// Порядок важен: модуль может использовать только то, что объявлено выше.
const ENGINE_FILES = ["util.js", "rules.js", "units.js", "morale.js", "combat.js", "turn.js"];

const RELEASE = {
  version: pkg.version.replace(/\.0$/, ""),     // 30.0.0 → 30.0
  update: "Пушки и крепости",
  patch: "Фундамент",
};

function bundleEngine(){
  const exported = [];
  const parts = ENGINE_FILES.map(f => {
    let code = read("src/engine/" + f);
    // import { a, b } from "./x.js";  — может занимать несколько строк
    code = code.replace(/^import\s*\{[\s\S]*?\}\s*from\s*["'][^"']+["'];?[ \t]*$/gm, "");
    code = code.replace(/^export\s+(const|let|function)\s+([A-Za-z_$][\w$]*)/gm, (m, kw, name) => {
      exported.push(name);
      return `${kw} ${name}`;
    });
    return `// ── ${f} ──\n${code.trim()}\n`;
  });
  const dup = exported.filter((n, i) => exported.indexOf(n) !== i);
  if(dup.length) throw new Error("двойной экспорт: " + dup.join(", "));
  return `const Engine = (function(){\n${parts.join("\n")}\nreturn { ${exported.join(", ")} };\n})();`;
}

// Вставка без спецсимволов замены ($&, $1) — в коде они встречаются
const put = (text, token, value) => text.split(token).join(value);

let html = read("src/ui/template.html");
html = put(html, "/*__ENGINE__*/", bundleEngine());
html = put(html, "/*__APP__*/", read("src/ui/app.js"));
html = put(html, "__PATCHNOTES__", read("PATCHNOTES.md").replace(/<\/script/gi, "<\\/script"));
html = put(html, "__VERSION__", RELEASE.version);
html = put(html, "__UPDATE__", RELEASE.update);
html = put(html, "__PATCHNAME__", RELEASE.patch);

fs.mkdirSync(new URL("dist/", ROOT), { recursive: true });
fs.writeFileSync(new URL("dist/tracker.html", ROOT), html);
console.log(`собрано: dist/tracker.html · v${RELEASE.version} «${RELEASE.patch}» · ${Math.round(html.length / 1024)} КБ`);
