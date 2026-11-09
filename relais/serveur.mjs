// Relais AMINJ : le seul endroit ou vit la cle OpenAI.
//
// Le casque ou le navigateur n'appelle jamais OpenAI directement : il appelle ce relais,
// qui ajoute la cle, applique les garde-fous et journalise les traces. Une cle placee
// dans un APK, une page web ou un depot Git est une cle publiee.
//
// Lancer :   node relais/serveur.mjs          (Node 18 ou plus, aucune dependance)
// Cle     :  variable OPENAI_API_KEY, ou fichier relais/.env (voir env.exemple)
//
// Routes
//   GET  /sante              etat du relais (sans la cle)
//   POST /api/chat           { question, contexte?, historique? }  ->  { reponse }
//   POST /api/trace          un enonce xAPI simplifie                ->  { ok }
//   POST /api/session-vocale jeton ephemere pour l'API Realtime     ->  { jeton, expire }
//   GET  /*                  fichiers du dossier web/ s'il existe (equipe web)

import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ICI = path.dirname(fileURLToPath(import.meta.url));
const RACINE = path.resolve(ICI, '..');

// ---------------------------------------------------------------- configuration
function lireEnv(fichier) {
  if (!fs.existsSync(fichier)) return;
  for (const ligne of fs.readFileSync(fichier, 'utf8').split(/\r?\n/)) {
    const m = ligne.match(/^\s*([A-Z0-9_]+)\s*=\s*(.*)\s*$/);
    if (m && !process.env[m[1]]) process.env[m[1]] = m[2].replace(/^["']|["']$/g, '');
  }
}
lireEnv(path.join(ICI, '.env'));

const CLE = process.env.OPENAI_API_KEY || '';
const MODELE = process.env.OPENAI_MODEL || 'gpt-4.1-mini';
const MODELE_VOIX = process.env.OPENAI_REALTIME_MODEL || 'gpt-realtime';
const PORT = Number(process.env.PORT || 8787);
const MAX_APPELS_JOUR = Number(process.env.MAX_APPELS_JOUR || 300);
const MAX_JETONS = Number(process.env.MAX_JETONS_REPONSE || 300);
const CONSIGNES = path.join(ICI, 'consignes_agent.md');
const JOURNAL = path.join(ICI, 'journal');
fs.mkdirSync(JOURNAL, { recursive: true });

if (!CLE) console.warn('[relais] Aucune cle : /api/chat et /api/session-vocale repondront 503.');

// ---------------------------------------------------------------- garde-fous
let jour = new Date().toISOString().slice(0, 10);
let appelsDuJour = 0;
const dernierAppel = new Map();           // ip -> horodatage

function autoriser(ip) {
  const aujourdhui = new Date().toISOString().slice(0, 10);
  if (aujourdhui !== jour) { jour = aujourdhui; appelsDuJour = 0; }
  if (appelsDuJour >= MAX_APPELS_JOUR) return 'plafond du jour atteint';
  const t = Date.now();
  if (t - (dernierAppel.get(ip) || 0) < 1000) return 'une requete par seconde au plus';
  dernierAppel.set(ip, t);
  appelsDuJour += 1;
  return null;
}

function journaliser(fichier, objet) {
  const ligne = JSON.stringify({ horodatage: new Date().toISOString(), ...objet });
  fs.appendFileSync(path.join(JOURNAL, `${fichier}-${jour}.jsonl`), ligne + '\n');
}

// ---------------------------------------------------------------- outils HTTP
function repondre(res, code, corps) {
  res.writeHead(code, {
    'Content-Type': 'application/json; charset=utf-8',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  });
  res.end(JSON.stringify(corps));
}

function lireCorps(req) {
  return new Promise((ok, ko) => {
    let s = '';
    req.on('data', (d) => { s += d; if (s.length > 64000) { ko(new Error('corps trop long')); req.destroy(); } });
    req.on('end', () => { try { ok(s ? JSON.parse(s) : {}); } catch (e) { ko(e); } });
  });
}

const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
  '.json': 'application/json', '.glb': 'model/gltf-binary', '.gltf': 'model/gltf+json', '.png': 'image/png',
  '.jpg': 'image/jpeg', '.svg': 'image/svg+xml', '.wasm': 'application/wasm' };

function servirStatique(req, res) {
  const web = path.join(RACINE, 'web');
  if (!fs.existsSync(web)) return repondre(res, 404, { erreur: 'pas de dossier web/' });
  const url = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  const fichier = path.normalize(path.join(web, url === '/' ? 'index.html' : url));
  if (!fichier.startsWith(web) || !fs.existsSync(fichier) || fs.statSync(fichier).isDirectory())
    return repondre(res, 404, { erreur: 'introuvable' });
  res.writeHead(200, { 'Content-Type': TYPES[path.extname(fichier)] || 'application/octet-stream', 'Cache-Control': 'no-cache' });
  fs.createReadStream(fichier).pipe(res);
}

// ---------------------------------------------------------------- appels OpenAI
async function appelerOpenAI(url, corps) {
  const r = await fetch(url, {
    method: 'POST',
    headers: { Authorization: `Bearer ${CLE}`, 'Content-Type': 'application/json' },
    body: JSON.stringify(corps),
  });
  const donnees = await r.json();
  if (!r.ok) throw new Error(donnees?.error?.message || `OpenAI ${r.status}`);
  return donnees;
}

async function chat({ question, contexte = '', historique = [] }) {
  if (!question || typeof question !== 'string') throw new Error('question manquante');
  const consignes = fs.existsSync(CONSIGNES) ? fs.readFileSync(CONSIGNES, 'utf8') : 'Tu es un agent pedagogique.';
  const messages = [
    { role: 'system', content: consignes },
    ...(contexte ? [{ role: 'system', content: `Contexte transmis par l'environnement :\n${contexte}` }] : []),
    ...historique.slice(-8).filter((m) => m && (m.role === 'user' || m.role === 'assistant')),
    { role: 'user', content: question.slice(0, 2000) },
  ];
  const d = await appelerOpenAI('https://api.openai.com/v1/chat/completions',
    { model: MODELE, messages, max_tokens: MAX_JETONS, temperature: 0.4 });
  return d.choices?.[0]?.message?.content?.trim() || '';
}

async function sessionVocale() {
  // Jeton ephemere : le client se connecte a l'API Realtime sans jamais voir la cle.
  const d = await appelerOpenAI('https://api.openai.com/v1/realtime/client_secrets',
    { session: { type: 'realtime', model: MODELE_VOIX } });
  return { jeton: d.value, expire: d.expires_at };
}

// ---------------------------------------------------------------- serveur
http.createServer(async (req, res) => {
  const ip = req.socket.remoteAddress || '?';
  const route = new URL(req.url, 'http://x').pathname;
  if (req.method === 'OPTIONS') return repondre(res, 204, {});
  try {
    if (req.method === 'GET' && route === '/sante')
      return repondre(res, 200, { ok: true, modele: MODELE, cle: Boolean(CLE), appelsDuJour, plafond: MAX_APPELS_JOUR });

    if (req.method === 'POST' && route === '/api/trace') {
      const enonce = await lireCorps(req);
      journaliser('traces', { ip, ...enonce });
      return repondre(res, 200, { ok: true });
    }

    if (req.method === 'POST' && (route === '/api/chat' || route === '/api/session-vocale')) {
      if (!CLE) return repondre(res, 503, { erreur: 'relais sans cle OpenAI' });
      const refus = autoriser(ip);
      if (refus) return repondre(res, 429, { erreur: refus });
      if (route === '/api/session-vocale') return repondre(res, 200, await sessionVocale());
      const corps = await lireCorps(req);
      const reponse = await chat(corps);
      journaliser('echanges', { ip, question: corps.question, contexte: corps.contexte, reponse });
      return repondre(res, 200, { reponse });
    }

    if (req.method === 'GET') return servirStatique(req, res);
    return repondre(res, 404, { erreur: 'route inconnue' });
  } catch (e) {
    return repondre(res, 400, { erreur: String(e.message || e) });
  }
}).listen(PORT, '0.0.0.0', () => {
  console.log(`[relais] http://localhost:${PORT}  modele ${MODELE}  plafond ${MAX_APPELS_JOUR} appels par jour`);
  console.log('[relais] Depuis le casque : remplacer localhost par l\'adresse IP de ce PC sur le meme reseau.');
});
