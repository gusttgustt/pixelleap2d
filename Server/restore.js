// Puts a backup of the player accounts and saves back (run by restore-backup.bat, with the server stopped).
//   node Server/restore.js           asks which backup
//   node Server/restore.js <folder>  that backup (a name from Server/backups, or a full path)
// What is in Server/data now is first copied to Server/backups/before-restore_<date_time>/, so nothing is lost.
"use strict";
const fs = require("fs");
const path = require("path");
const readline = require("readline");

const DATA_DIR = path.resolve(process.env.DATA_DIR || path.join(__dirname, "data"));
const BACKUP_DIR = path.resolve(process.env.BACKUP_DIR || path.join(__dirname, "backups"));

function stamp() {
  const d = new Date(), p = n => String(n).padStart(2, "0");
  return d.getFullYear() + "-" + p(d.getMonth() + 1) + "-" + p(d.getDate()) + "_" + p(d.getHours()) + "-" + p(d.getMinutes()) + "-" + p(d.getSeconds());
}
const jsonIn = dir => { try { return fs.readdirSync(dir).filter(f => f.endsWith(".json")); } catch (e) { return []; } };

function players(dir) {
  try { return Object.keys(JSON.parse(fs.readFileSync(path.join(dir, "accounts.json"), "utf8"))).length; } catch (e) { return -1; }
}

function restore(from) {
  const files = jsonIn(from);
  if (!files.includes("accounts.json") || players(from) < 0) { console.log("ไม่ใช่ข้อมูลสำรองที่ใช้ได้: " + from); return false; }
  const now = jsonIn(DATA_DIR);
  if (now.length) {
    const keep = path.join(BACKUP_DIR, "before-restore_" + stamp());
    fs.mkdirSync(keep, { recursive: true });
    for (const f of now) fs.copyFileSync(path.join(DATA_DIR, f), path.join(keep, f));
    console.log("เก็บข้อมูลปัจจุบันไว้ก่อนที่: " + keep);
    for (const f of now) fs.rmSync(path.join(DATA_DIR, f));
  }
  fs.mkdirSync(DATA_DIR, { recursive: true });
  for (const f of files) fs.copyFileSync(path.join(from, f), path.join(DATA_DIR, f));
  console.log("กู้คืนแล้ว: " + files.length + " ไฟล์, " + players(DATA_DIR) + " บัญชี จาก " + from);
  return true;
}

const arg = process.argv[2];
if (arg) {
  process.exit(restore(path.isAbsolute(arg) ? arg : path.join(BACKUP_DIR, arg)) ? 0 : 1);
}

let list = [];
try {
  list = fs.readdirSync(BACKUP_DIR).map(d => ({ d, s: fs.statSync(path.join(BACKUP_DIR, d)) }))
    .filter(x => x.s.isDirectory()).sort((a, b) => b.s.mtimeMs - a.s.mtimeMs).map(x => x.d);
} catch (e) {}
if (!list.length) { console.log("ยังไม่มีข้อมูลสำรองใน " + BACKUP_DIR); process.exit(1); }
console.log("ข้อมูลสำรอง (ใหม่สุดอยู่บน):");
list.forEach((d, i) => console.log("  " + String(i + 1).padStart(2) + ")  " + d + "   (" + players(path.join(BACKUP_DIR, d)) + " บัญชี)"));
const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
rl.question("\nพิมพ์หมายเลขที่จะกู้คืน (หรือกด Enter เพื่อยกเลิก): ", a => {
  rl.close();
  const n = parseInt(a, 10);
  if (!(n >= 1 && n <= list.length)) { console.log("ยกเลิก ไม่ได้เปลี่ยนอะไร"); process.exit(1); }
  process.exit(restore(path.join(BACKUP_DIR, list[n - 1])) ? 0 : 1);
});
