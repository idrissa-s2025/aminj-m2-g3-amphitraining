# AmphiTraining (G3)

Socle de projet du Master 2 AMINJ, INU Champollion, 2026-2027.
Module « Développer une ingénierie pédagogique pour les serious games ».

Ce dépôt n'est pas le projet AmphiTraining complet. C'est une **base de départ ciblée** :
l'amphithéâtre, son éclairage, les robots du public, l'écran de présentation et une brique
d'agent OpenAI propre. Ce qui en fait une **formation à la prise de parole** reste à
concevoir par le groupe.

## Ce que contient le socle

| Élément | Où | État |
|---|---|---|
| Amphithéâtre de 228 places, éclairage cuit | `Assets/Amphi/Scenes/Amphi.unity`, `Model/`, `Textures/` | fourni |
| Robots du public (modèle et matériaux) | `Assets/Amphi/Robot/` | fourni, sans comportement |
| Rig VR, déplacement, sièges | `Assets/Amphi/Runtime/` | fourni |
| Écran et panneau de présentation (PDF) | `Assets/Amphi/Runtime/Presentation/` | fourni |
| Affiches et logo | `Assets/Amphi/Resources/` | fourni |
| Agent par le relais (texte) | `AgentRelais.cs` et `relais/` | fourni, fonction didactique à écrire |
| Une question du public (démonstration, touche Q) | `QuestionDuPublic.cs` | fourni |
| Traces xAPI simplifiées | `TraceXapi.cs`, écrites par le relais | fourni, verbes à choisir |

## Ce qui n'y est pas, volontairement

Les rôles des dix robots, leurs réactions, le moment où le public intervient, la voix,
l'écoute de l'orateur, les critères de l'oral, la progression et le débriefing : voir
[`docs/A_CONSTRUIRE.md`](docs/A_CONSTRUIRE.md).

## Démarrer

1. **Unity 6000.6.0f1** avec le module *Android Build Support*. Ouvrir le dossier depuis
   Unity Hub, dans un chemin court (par exemple `C:\aminj\g3`).
2. Menu **AMINJ > 1. Préparer la scène du socle**, puis ouvrir `Assets/Amphi/Scenes/Amphi.unity`.
3. **Relais** (Node 18 ou plus) : copier `relais/env.exemple` en `relais/.env`, y coller la clé
   fournie par l'enseignant, lancer `node relais/serveur.mjs`.
4. *Play* dans l'éditeur, puis touche **Q** : le public pose une question sur le résumé saisi
   dans *QuestionDuPublic*. Dans le casque, renseigner l'adresse IP du PC dans *AgentRelais* et
   *TraceXapi*.

## L'agent IA : où tout se trouve

L'agent passe toujours par le relais : le client (casque ou navigateur) envoie une question et
un contexte, le relais ajoute la clé et les consignes, puis appelle OpenAI.

| Ce qui | Fichier | Ce que le groupe y fait |
|---|---|---|
| Appel à OpenAI, clé, modèle, plafonds | `relais/serveur.mjs` | rien en principe ; modèle et plafonds se règlent dans `relais/.env` |
| Clé et réglages (`OPENAI_API_KEY`, `OPENAI_MODEL`, `MAX_APPELS_JOUR`) | `relais/.env`, copié de `relais/env.exemple` | coller la clé ; jamais dans Git |
| **Instruction** de l'agent (message système) | `relais/consignes_agent.md` | **à réécrire** : rôle, ce qu'il dit, ce qu'il ne dit jamais, forme |
| Côté Unity : envoi de la question et réception de la réponse | `Assets/AMINJ/Scripts/Runtime/AgentRelais.cs` | brancher la voix, l'animation des robots |
| Quand le public pose une question, et laquelle | `Assets/AMINJ/Scripts/Runtime/QuestionDuPublic.cs` (touche Q, `DemanderUneQuestion()`) | décider du moment et du rôle |
| **Connaissances** transmises : le résumé de l'exposé | champ `resumeDemo` de `QuestionDuPublic`, à remplacer par le vrai contenu de l'exposé | définir ce que le public « sait » |
| Traces xAPI envoyées au relais | `Assets/AMINJ/Scripts/Runtime/TraceXapi.cs` | choisir les verbes |
| Journal des échanges et des traces | `relais/journal/*.jsonl` | lire pour l'évaluation et le débriefing |

Le relais relit `consignes_agent.md` à chaque question : une modification s'applique sans
redémarrer. Les connaissances ne sont pas « apprises » par le modèle : elles lui sont
transmises à chaque échange dans le contexte. Ce qui n'est pas dans le contexte, l'agent doit
dire qu'il ne le sait pas.

## Règles

- **La clé OpenAI ne quitte jamais le relais.** `relais/.env` est ignoré par Git.
- Aucune donnée personnelle réelle ; aucun enregistrement de voix sans consentement.
- AI Act, article 5 : simuler les émotions des robots est permis ; inférer les émotions
  réelles de l'apprenant est interdit en éducation.
- L'agent annonce qu'il est une IA (article 50).

## Structure

```
Assets/AMINJ/Scripts/Runtime   AgentRelais, TraceXapi, QuestionDuPublic
Assets/AMINJ/Scripts/Editor    PreparerSocle (menu AMINJ)
Assets/Amphi                   salle, robots, rig, présentation
relais/                        serveur.mjs, consignes_agent.md, env.exemple
docs/A_CONSTRUIRE.md           ce que le groupe conçoit
```
