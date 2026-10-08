# MaintenanceAddon — Gestion de la maintenance pour SAP Business One

Add-on classique SAP Business One (UI API + DI API, SQL Server) qui reprend
les objets et le cycle de vie du module **Maintenance (PM / EAM) de SAP S/4HANA** :

| S/4HANA | Transaction | Add-on (menu **Modules → Maintenance**) |
|---|---|---|
| Poste technique | IL01/IL02 | Données de base → Postes techniques |
| Équipement | IE01/IE02 | Données de base → Équipements (article et n° de série SAP, pièces de rechange, documents, points de mesure, historique, KPI 12 mois) |
| Nomenclature d'équipement (pièces de rechange) | IB01 | Équipement → onglet Pièces de rechange ; Ordre → Pièces de l'équipement |
| Bon de travail | IW3M / impression d'ordre | Ordre → Bon de travail (page imprimable) |
| Autorisations PM | Rôles PFCG | Autorisations SAP : Maintenance → 6 autorisations |
| Poste de travail | IR01 | Données de base → Postes de travail (taux horaire, capacité) |
| Gamme | IA05/IA06 | Données de base → Gammes de maintenance (opérations + pièces) |
| Catalogues | QS41 | Données de base → Catalogues (partie d'objet, dommage, cause, activité) |
| Avis M1/M2/M3 | IW21/IW22 | Avis de maintenance |
| Ordre PM01..PM04 | IW31/IW32 | Ordres de maintenance |
| Statuts CRTD/REL/TECO/CLSD | — | Boutons Lancer / Clôture technique / Clôturer |
| Sortie de pièces (261/262) | MIGO | Ordre → Composants → Sortie / Retour de stock |
| Demande d'achat | ME51N | Ordre → Demandes d'achat (pièces non stockées, prestations) |
| Confirmation | IW41 | Ordre → Opérations → Confirmer du temps |
| Point de mesure / relevé | IK01/IK11 | Relevés de compteurs et mesures |
| Plan temps / compteur | IP41/IP42 | Maintenance préventive → Plans de maintenance |
| Ordonnancement | IP10/IP30/IP24 | Maintenance préventive → Ordonnancement des plans |
| Contrat de maintenance / garantie | BP_WAR, contrats de service | Données de base → Contrats de maintenance ; garantie sur la fiche équipement |
| Réparation externe (envoi / retour) | Sous-traitance, IW8W | Envoi / retour chez un prestataire |
| Règlement de l'ordre sur immobilisation | KO88 | Ordre « À immobiliser » → écriture de règlement à la clôture |
| Listes, analyses | IW39, IW29, IH01, MCI* | Rapports (15 vues) |
| Lien PP / PM (ordre de fabrication, arrêts de ligne, compteurs de production) | CO03, IK11 auto | Maintenance de la production (par OF) ; bouton Maintenance sur l'OF SAP |

## 1. Compiler et lancer

Sur le serveur (DI API / UI API 10.0 x64) :

```
"C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe" src\MaintenanceAddon\MaintenanceAddon.csproj -restore
```

- Plateforme **x64**, .NET Framework 4.8. Les types SAP sont intégrés à l'exe
  (`EmbedInteropTypes=True`) : le package ne contient que `MaintenanceAddon.exe`.
- Débogage : client SAP B1 ouvert sur `TST_TST`, puis **F5** dans Visual Studio
  (Debug | x64) ; la chaîne de connexion de développement est dans `MaintenanceAddon.csproj.user`.
- Production : Release | x64, puis enregistrement du package dans
  **Administration → Add-Ons → Add-On Administration**.
- Journal de démarrage et d'erreurs : `%TEMP%\MaintenanceAddon.log`.

## 2. Premier démarrage

L'add-on crée ce qui manque (idempotent, à chaque démarrage) :

- 22 tables `@MNT_*` et leurs champs (la table des pièces de rechange `@MNT_EQP2` est
  ajoutée à l'objet équipement d'une installation existante) ;
- 10 objets UDO : `MNT_FLOC`, `MNT_EQUIP`, `MNT_WCTR`, `MNT_CATAL`, `MNT_EQCAT`,
  `MNT_TASKL`, `MNT_PLAN`, `MNT_CONTR` (données de base), `MNT_NOTIF`, `MNT_ORDER` (documents,
  avec une série « Primaire » créée automatiquement — modifiable dans *Gestion → Initialisation → Numérotation des documents*) ;
- les champs `U_MNT_Ord` / `U_MNT_Line` (ordre imputé) et `U_MNT_Ctr` (contrat facturé)
  sur les lignes des documents marketing (sorties, entrées, demandes, commandes, factures et avoirs fournisseurs) ;
- un paramétrage par défaut et des données d'exemple (catalogues, 5 catégories,
  3 postes de travail) ;
- les **autorisations** « Maintenance » dans l'arbre des autorisations SAP (voir § 3).

Les autres utilisateurs connectés doivent ensuite se reconnecter.

## 3. Paramétrage (Maintenance → Paramètres)

- **Compte de charges de maintenance** : contrepartie des sorties de pièces et
  des lignes de prestation des demandes d'achat. Une catégorie d'équipement peut
  avoir son propre compte.
- **Main-d'oeuvre** (option) : si cochée, chaque confirmation passe une écriture
  *Débit charges MO (centre de coûts de l'ordre) / Crédit imputation MO* pour
  heures × taux du poste de travail.
- **Axe analytique** : dimension (1 à 5) des centres de coûts portés par les
  postes techniques, équipements et ordres.
- **Délais par priorité** : fin souhaitée / prévue = début + délai.
- Postes de travail : renseigner les **taux horaires** (coûts prévus et réels).
- **Alertes** : destinataires (codes utilisateurs SAP) et horizon en jours. Les alertes
  arrivent dans la **messagerie interne SAP** : chaque avis urgent (priorité 1 ou arrêt),
  et une fois par jour (premier démarrage de l'add-on de la journée, ou bouton *Envoyer les
  alertes maintenant*) : préventifs en retard, ordres en retard, garanties qui expirent,
  contrats à décider (fin ou préavis), équipements non revenus de chez le prestataire.
- **Autorisations** (*Gestion → Initialisation système → Autorisations → Autorisations
  générales → Autorisations utilisateur → Maintenance*), chacune *complète / lecture seule /
  aucune* :

  | Autorisation | Contrôle |
  |---|---|
  | Données de base | Postes techniques, équipements (et documents joints), gammes, plans, contrats |
  | Avis de maintenance | Déclarer, modifier, terminer, rouvrir |
  | Ordres | Créer, modifier, lancer, demandes d'achat, appels de plans |
  | Exécution | Confirmations de temps, sorties / retours de pièces, relevés, envois / retours |
  | Clôture | Clôture technique (et son annulation), clôture, annulation des ordres |
  | Paramètres | Paramètres de la maintenance |

  Lecture seule : l'écran s'ouvre mais n'enregistre pas. Les **super-utilisateurs** ont
  tous les droits ; SAP donne « aucune » aux autres utilisateurs à la création : l'administrateur
  doit les attribuer après l'installation (ex. technicien : Avis + Exécution complètes, Ordres en lecture).

## 4. Processus

### Correctif
1. **Avis** (M2 panne) sur un équipement : description, priorité, arrêt
   machine (début / fin de panne), codes catalogue.
2. **Créer l'ordre** depuis l'avis (PM01, opération 0010 par défaut ; l'avis passe *En cours*).
3. Compléter l'ordre : opérations (poste, heures, clé INT/EXT), composants
   (article, quantité, magasin, Stock/Achat) ou **Reprendre la gamme**.
4. **Lancer** (REL) → **Sortie de stock**, **Demandes d'achat**, **Confirmer du temps**.
5. **Clôture technique** (TECO) : termine l'avis, fixe la fin réelle, met à jour le plan.
6. **Clôturer** (CLSD) quand les achats sont reçus et facturés.

### Préventif
1. Plan **temps** (tous les N jours / semaines / mois / ans) ou **compteur**
   (toutes les N unités d'un point de mesure compteur), avec gamme et horizon d'appel.
2. **Ordonnancement** : liste des plans, état (*À appeler*, *Pas encore dû*,
   *Ordre en cours*) ; appel unitaire ou en masse → ordres PM02 avec opérations et pièces de la gamme.
3. Échéance suivante : depuis la **date planifiée** (dès l'appel) ou depuis la
   **clôture technique** (à la TECO de l'ordre). **Ignorer l'échéance** saute un cycle.

### Prestataires, garanties, contrats (ex. climatiseurs)
- **Site externe** (agence, dépôt) : un poste technique avec son adresse ; les
  appareils héritent de son centre de coûts et de son magasin (toute la hiérarchie).
- **Fiche équipement** : fournisseur d'achat, **garant** (si différent), **fin de
  garantie**, **prestataire de maintenance attitré**. Un bandeau signale garantie,
  contrat actif et envoi en cours.
- **Garantie** : un ordre correctif (PM01) sur un appareil sous garantie est marqué
  « sous garantie » ; l'opération par défaut est confiée au garant, sans coût ; aucune
  demande d'achat n'est émise pour la prestation couverte.
- **Contrat de maintenance** : prestataire, période, préavis, montant annuel et
  périodicité de facturation, délai d'intervention, main-d'oeuvre / pièces incluses,
  équipements couverts (un seul contrat actif par équipement et par période).
  Type *Préventif* (PM02/PM04), *Dépannage* (PM01) ou *Complet* ; les améliorations
  (PM03) sont toujours hors contrat. Un ordre couvert est rattaché au contrat ; sa fin
  prévue respecte le délai d'intervention ; les prestations couvertes sont à coût 0 et
  ne génèrent pas de demande d'achat. Les factures du contrat portent son code
  (`U_MNT_Ctr`) : la fiche compare **facturé** et **prévu au prorata**.
- **Prestataire par défaut** d'une opération externe : garant si sous garantie, sinon
  titulaire du contrat, sinon prestataire attitré.
- **Envoi / retour** : l'équipement passe « Chez le prestataire » à l'envoi (avec retour
  prévu, ordre lié) et reprend son statut au retour ; double envoi et retour sans envoi refusés.
- **Immobilisation** : un ordre « À immobiliser » (coché d'office en PM03) est **réglé** à la
  clôture : une écriture transfère ses coûts comptabilisés (sorties nettes, factures nettes,
  main-d'oeuvre comptabilisée) des comptes de charges (avec le centre de coûts de l'ordre)
  vers le compte de règlement de l'ordre ou le compte d'immobilisations en cours des paramètres.
  Le module Immobilisations de SAP n'étant pas utilisé dans la société, l'activation de la fiche
  d'immobilisation reste à faire par la comptabilité.

### Mesures
- Relevé de compteur : jamais en baisse, saisie chronologique.
- Mesure hors limites (basse / haute du point) : proposition d'avis automatique.

### Lien avec la production SAP (ordres de fabrication)
Les ressources SAP n'étant pas utilisées, le lien OF ↔ machines passe par la **ligne de production** :
- **Ligne** = poste technique coché *Ligne de production* ; ses **machines** = équipements installés
  sur la ligne ou ses sous-postes.
- **Ligne d'un OF** = champ utilisateur `U_MNT_PLine` de l'OF (*Ligne de production (maint.)*), sinon
  la ligne par défaut de l'article produit (`OITM.U_MNT_PLine`). Le code saisi sur l'OF est contrôlé à
  l'enregistrement (SAP refuse le lien de champ vers l'objet poste technique, erreur -5002).
- **Écran SAP « Ordre de fabrication »** (type 65211, aiguillé par `EventHub.RegisterFormType`) :
  bouton **Maintenance** (suivi de l'OF) ; au passage au statut *Lancé*, liste des points à vérifier
  sur la ligne (machine hors service ou chez un prestataire, panne en cours, préventif en retard, pièce
  de rechange insuffisante) et confirmation.
- **Avis** : onglet *Production* : OF (proposé automatiquement : OF lancé le plus récent de la ligne de
  la machine), *ligne de production arrêtée*, *quantité perdue*. Repris sur l'ordre de maintenance.
- **Suivi de l'OF** (*Maintenance → Maintenance de la production*, ou bouton de l'OF, ou double-clic dans
  le rapport) : synthèse (avis, ordres, arrêt de production = durée des pannes « ligne arrêtée »,
  quantité perdue, coût réel des ordres), points à vérifier, interventions **rattachées à l'OF** ou
  **sur une machine de la ligne pendant l'OF** (début → clôture de l'OF), pièces consommées par ces
  interventions, pièces de rechange des machines de la ligne avec leur stock.
- **Rapport** *Maintenance par ordre de fabrication* (interventions rattachées).
- **Compteurs alimentés par la production** : un point de mesure compteur coché *Par la production*
  reçoit la quantité de chaque entrée de production (produit de l'OF, tous types d'entrée) de sa ligne :
  plans préventifs « toutes les N unités » sans saisie. Synchronisation au démarrage de l'add-on, à
  l'ordonnancement et à l'envoi des alertes ; repère `@MNT_SETUP.U_ProdSync` (la première fois : à
  partir des entrées suivantes) et clé de ligne `U_SrcDoc` sur le relevé (pas de double comptage). Une
  annulation d'entrée ne fait pas baisser le compteur.

### Intégration aux données SAP
La fiche équipement reste l'objet central de la maintenance (comme IE01 dans S/4HANA),
qu'un module Immobilisations soit utilisé ou non ; elle **se rattache** aux données SAP :
- **Article SAP** (articles hors immobilisations) et **n° de série reçu dans SAP** : le bouton
  *N° reçus...* (onglet Données techniques) liste les n° de série reçus (réception de
  marchandises, facture fournisseur, entrée de marchandises). Le choix d'un n° complète la
  fiche sans ressaisie : article, n°, fabricant de l'article, fournisseur, date et valeur
  d'achat (montant de la ligne / quantité), fin de garantie et année de fabrication du n° de
  série. En création, c'est la façon la plus rapide de créer un équipement acheté.
- **Doublons refusés** : un même n° de série (même article, ou même fabricant sans article)
  ou une même immobilisation ne peut appartenir qu'à un équipement.
- **Immobilisation SAP** : champ affiché seulement si la société a des articles de type
  immobilisation (module utilisé) ; sinon, valeur et date d'acquisition suffisent, et le
  règlement des ordres « À immobiliser » passe par le compte d'immobilisations en cours.
- **Pièces de rechange** (onglet de la fiche) : articles SAP montés sur l'équipement. Dans
  l'ordre, *Pièces de l'équipement...* les propose avec le stock disponible du magasin et
  les ajoute aux composants. Rapport *Pièces de rechange des équipements (stock)* : stock
  inférieur à la quantité montée ou au stock mini de l'article.
- **Documents** (onglet de la fiche) : notices, photos, schémas, certificats dans les
  **pièces jointes standard de SAP** (dossier des pièces jointes de la société, table ATC1),
  fichiers préfixés par le code de l'équipement. *Retirer* reconstruit la pièce jointe sans le
  fichier (l'API ne supprime pas de ligne) ; le fichier reste dans le dossier, comme dans SAP.
- **Bon de travail** : depuis l'ordre enregistré, page HTML ouverte dans le navigateur pour
  impression (appareil, lieu, garantie / contrat, travail demandé, **consignes de sécurité**,
  opérations, pièces, relevés à noter, compte rendu, signatures). Les consignes de sécurité se
  saisissent sur la gamme (onglet Sécurité) et sont recopiées sur l'ordre.

## 5. Règles de gestion

| Règle | Détail |
|---|---|
| Statuts de l'ordre | CRTD → REL → TECO → CLSD ; annulation possible en CRTD/REL sans aucune imputation ; TECO annulable |
| Modifications | Ordre modifiable en CRTD et REL uniquement |
| Lignes protégées | Opération avec temps confirmé, composant sorti ou ligne avec demande d'achat : non supprimable |
| Sorties de stock | Ordre lancé, articles gérés en stock ; retour ≤ quantité nette sortie, valorisé au coût de sortie |
| Coûts prévus | heures × taux du poste (internes) + coût prévu (externes) + quantités × coût moyen article |
| Coûts réels | confirmations + sorties nettes des retours + factures fournisseurs (lignes imputées, hors articles stockés) − avoirs |
| Clôture (CLSD) | refusée tant qu'une demande ou commande d'achat liée est ouverte |
| Avis | M3 (rapport d'activité) ne génère pas d'ordre ; un avis lié à un ordre non clôturé ne peut pas être terminé seul |
| Suppression | Données de base avec historique : non supprimables (passer « Mis au rebut » / inactif) ; documents : jamais supprimés |
| Comptes | Les comptes saisis dans l'add-on doivent être imputables, non bloqués et non collectifs |
| Équipements | N° de série et immobilisation uniques ; un n° de série saisi à la main est rattaché au n° SAP s'il a été reçu |
| Autorisations | Contrôlées à l'ouverture et à l'enregistrement des écrans, et dans chaque action des services (même hors écran) |
| Transactions | Chaque action (ordre + documents SAP + écritures + statuts liés) est faite dans une transaction DI API |

Indicateurs (rapport *Indicateurs équipements*), sur la période choisie :
- **Arrêt** = somme des durées de panne des avis avec *arrêt machine* (une panne en cours compte jusqu'à maintenant) ;
- **MTTR** = arrêt / nombre de pannes ; **MTBF** = (heures de la période − arrêt) / nombre de pannes ;
- **Disponibilité** = (heures de la période − arrêt) / heures de la période.

Imputation des achats : sur les lignes de commande / facture fournisseur, le
champ **Ordre de maintenance** (`U_MNT_Ord`) rattache le coût à l'ordre (repris
automatiquement depuis la demande d'achat quand la commande est faite par copie).

## 6. Structure du code

```
src/MaintenanceAddon/
  Program.cs                  Démarrage, menus, aiguillage des menus
  Constants.cs                Tables (@MNT_*), objets UDO, champs des documents SAP
  Core/DiCompany.cs           Connexion DI API (cookie), transactions
  Core/Sql.cs                 Lecture SQL (Recordset), conversions
  Core/Udo.cs                 Lecture / écriture des UDO (GeneralService)
  Core/Ui.cs                  Construction des écrans par code
  Core/UdoForm.cs             Base des écrans liés à un UDO (modes SAP, matrices, CFL, onglets, liens)
  Core/SimpleForm.cs          Base des popups / rapports
  Setup/MetadataSetup.cs      Création idempotente tables, champs, UDO, données de départ
  Models/Codes.cs             Statuts, types, priorités (codes + libellés)
  Services/OrderService.cs    Ordres : création, statuts, stock, achats, confirmations, coûts
  Services/NotificationService.cs  Avis, informations équipement
  Services/PlanService.cs     Plans, échéances, appels
  Services/MeasurementService.cs   Points de mesure, relevés
  Services/ReportService.cs   Requêtes des rapports
  Services/ContractService.cs Contrats, garantie / contexte d'intervention, envois chez un prestataire
  Services/EquipmentService.cs     N° de série SAP, immobilisation, doublons, pièces de rechange
  Services/AttachmentService.cs    Documents de l'équipement (pièces jointes SAP)
  Services/AlertService.cs    Alertes par la messagerie interne SAP
  Services/AuthService.cs     Autorisations (arbre des autorisations SAP)
  Services/WorkOrderPrint.cs  Bon de travail imprimable (HTML)
  Services/ProductionService.cs    Lignes, OF, suivi par OF, points à vérifier, compteurs de production
  Forms/ProductionViewForm.cs Suivi de l'OF + bouton et contrôle sur l'écran SAP de l'OF
  Forms/*.cs                  Écrans
```

La logique métier est entièrement dans `Services/` : c'est là qu'il faut lire en premier.

Guide utilisateur : `docs/Guide-utilisateur-Maintenance.html`.

## 7. Banc de test (DI API, sans client SAP)

`tests/MntTest` rejoue toute la logique métier sur une vraie société
(par défaut `TST_TST`, identifiants lus dans `SAPMover\SAPMover\App.config`) :
schéma, paramètres, données de base, avis → ordre, lancement, sortie / retour
de stock, confirmations et écritures, demandes d'achat, TECO / annulation de
TECO / clôture, annulation, relevés, plans temps / compteur / base clôture,
toutes les requêtes de rapports. Chaque exécution crée ses propres codes
(suffixe horaire) ; stock et écritures de main-d'oeuvre sont remis à zéro par le test.

```
"C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe" tests\MntTest\MntTest.csproj -restore
tests\MntTest\bin\MntTest.exe
```

Le test couvre aussi garantie, contrats, prestataire attitré, envoi / retour, et le
circuit demande d'achat → commande → facture fournisseur → coût réel → règlement
(facture et règlement contre-passés en fin de test). Nb : par la DI API, une ligne
de service copiée ne reprend pas son montant ; le test le renseigne (le client SAP le reprend).

Il couvre enfin l'intégration SAP : réception d'un climatiseur géré par n° de série
(article `MNTTEST_SER` créé au premier passage) → équipement créé depuis le n° reçu,
doublons, pièces de rechange et leur rapport, consignes de sécurité et bon de travail,
documents joints (fichiers de test retirés du dossier partagé à la fin), autorisations,
alertes (messages dans la messagerie de l'utilisateur de test).

Production : ligne de test (3 machines dont une dans une zone de la ligne), OF de test sur l'article
`FCAS_CEL_1` (composants passés en sortie manuelle, puis OF clôturés), ligne par défaut de l'article
(remise à sa valeur d'origine), entrée de production → compteur → plan dû, avis « ligne arrêtée »,
ordre rattaché, pièces, rapport par OF, points à vérifier avant lancement.

Dernier passage (08/10/2026) : **245 OK, 0 KO**.

### Banc des écrans (client SAP ouvert)

`tests/MntUiTest` pilote le client SAP B1 déjà ouvert sur `TST_TST` (chaîne de
connexion de développement, comme F5) : ouvre tous les menus, puis crée à l'écran
poste technique → équipement (point de mesure) → avis → ordre, ajoute des opérations,
lance, confirme du temps, TECO, clôture ; vérifie les refus d'enregistrement (aucun
document vide), la gamme, les paramètres et chaque rapport. Les boîtes de message
passent par `Program.Message` (réponse automatique « Oui ») ; les erreurs de la barre
d'état et du journal font échouer l'étape. Il refuse de démarrer si un autre banc
d'écrans (`*UiTest`) tourne (même client). Le choix et l'ouverture de fichier passent par
`Program.PickFileHook` / `Program.OpenFileHook`. Il teste aussi les pièces de rechange, les
documents joints, la création d'un équipement depuis un n° de série reçu (et le refus du
doublon), les pièces de l'équipement dans l'ordre, les consignes de sécurité, le bon de
travail, l'avis urgent et l'envoi des alertes.

```
"C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe" tests\MntUiTest\MntUiTest.csproj -restore
tests\MntUiTest\bin\MntUiTest.exe
```

Données laissées (suffixe horaire) : postes UIF*, équipements UIE*, avis, ordres
clôturés ou annulés, équipements UIS* créés depuis un n° de série (réception d'achat de
test) ; la gamme UIG* est supprimée. Dernier passage (07/10/2026) : **141 OK, 0 KO**.

Pièges UI API corrigés grâce à ce banc (socle `Core` identique à l'add-on Paie) :
un seul abonnement aux événements (`Core/EventHub.cs`, sinon un refus de `Validate()`
est ignoré) ; pas de réouverture après création (standard SAP, valeurs par défaut
remises au premier événement suivant) ; combo lié à des valeurs valides SAP non vidé ;
enregistrement vide gardé par SAP dans les lignes en création (retiré avant l'ajout
d'une ligne) ; case à cocher de matrice : Y/N posés avant la liaison ; table de grille
non recréée ; champ numérique utilisateur non vidable (`Invalid field value`).

## 8. Limites connues

- SQL Server uniquement (requêtes T-SQL).
- Une seule fenêtre ouverte par type d'objet (comme la majorité des écrans d'add-on).
- Volontairement hors périmètre (module simple) : gestion de capacité / planification
  graphique, stratégies multi-cycles, tournées d'inspection, budgets, permis de travail
  détaillés, amortissements (le lien vers l'immobilisation SAP est une référence).
- Le bon de travail est une page HTML (pas d'état Crystal) : l'impression passe par le navigateur.
- Unité de stock uniquement pour les composants (pas d'unités de mesure secondaires).
- Les règles sont appliquées par l'add-on : une écriture directe en base ou une
  modification hors add-on n'est pas contrôlée.
