// ═══════════ art-server.mjs — выгрузка рисунка полигона в проект Unity ═══════════
// node game/Tools/art-server.mjs [порт]   (по умолчанию 8790)
// Отдаёт собранный полигон (core/polygon/polygon.html) и принимает POST /save?file=<имя> — пишет тело запроса
// в game/Assets/Resources/Art/<имя>. Имена — только буквы, цифры, «-», «_», «.» и расширения .png/.json.
// Страница полигона в браузере рисует атласы теми же функциями, что рисуют бойцов (core/Tests/atlas-export.js),
// и шлёт их сюда — так у игры и полигона один источник рисунка.
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..", "..");                       // корень репозитория (worktree)
const serveDir = path.join(root, "core", "polygon");
const toolsDir = path.join(root, "core", "Tests");
const outDir = path.join(root, "game", "Assets", "Resources", "Art");
const port = +process.argv[2] || 8790;
const TYPES = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".json": "application/json", ".png": "image/png" };

http.createServer((req, res) => {
  const url = new URL(req.url, "http://localhost");
  if (req.method === "POST" && url.pathname === "/save") {
    const name = url.searchParams.get("file") || "";
    if (!/^[\w.-]+\.(png|json)$/.test(name)) { res.writeHead(400); res.end("плохое имя"); return; }
    const chunks = [];
    req.on("data", c => chunks.push(c));
    req.on("end", () => {
      fs.mkdirSync(outDir, { recursive: true });
      const buf = Buffer.concat(chunks);
      fs.writeFileSync(path.join(outDir, name), buf);
      console.log(`сохранено ${name}: ${buf.length} байт`);
      res.writeHead(200, { "Content-Type": "text/plain; charset=utf-8" }); res.end("ok");
    });
    return;
  }
  // файлы: собранный полигон и скрипт выгрузки
  const rel = decodeURIComponent(url.pathname).replace(/^\/+/, "") || "polygon.html";
  const base = rel === "atlas-export.js" ? toolsDir : serveDir;
  const file = path.join(base, rel);
  if (!file.startsWith(base) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404); res.end("нет"); return; }
  res.writeHead(200, { "Content-Type": TYPES[path.extname(file)] || "application/octet-stream" });
  fs.createReadStream(file).pipe(res);
}).listen(port, () => console.log(`рисунок для Unity: http://localhost:${port}/ → ${outDir}`));
