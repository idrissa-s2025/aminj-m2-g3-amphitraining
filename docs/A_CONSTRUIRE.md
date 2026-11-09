# Ce que le groupe P3 conçoit

Le socle montre une chaîne technique : un résumé d'exposé part au relais, une question
revient du « public ». Il ne fait rien apprendre. Les sept éléments ci-dessous sont ceux du
dossier du module ; chacun indique l'endroit du code où il se branche.

## 1. Objectif d'apprentissage

Qu'est-ce qui est appris à travers le dispositif, et comment l'orateur prend-il conscience de
l'évolution de sa prise de parole ? Préciser le public visé (doctorants, étudiants de master,
formateurs) et la situation (soutenance, colloque, cours).

## 2. Critères de l'oral et progression

Quels critères (structure, temps, réponse aux questions, regard) et quelle progression d'une
séance à l'autre (public bienveillant, puis exigeant).

## 3. Le public : rôles et moments

Les dix robots n'ont aucun rôle : à définir (novice, spécialiste, contradicteur
méthodologique…), avec le moment où chacun intervient. Point d'entrée :
`QuestionDuPublic.DemanderUneQuestion()` et `relais/consignes_agent.md`.

## 4. Ce qui est observé de l'oral

Transcription de la parole, temps de parole, diapositive affichée : ce que le système observe
et ce qu'il n'a pas le droit d'inférer (AI Act, article 5). Le relais fournit
`POST /api/session-vocale` pour l'API Realtime ; la chaîne vocale reste à construire.

## 5. Traces

Choisir les verbes avant de développer : `a_recu_une_question` est fourni, à compléter
(`a_change_de_diapositive`, `a_repondu`…). Elles sont écrites dans `relais/journal/`.

## 6. Évaluation

Grille de l'oral, épreuves avant et après, questionnaires (SUS, MPS, SSQ, ASAQ).

## 7. Débriefing

Ce que l'orateur voit de sa prestation à la fin : questions reçues, temps, critères.
