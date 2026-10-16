# Fais ton mème

Jeu web multijoueur de mèmes. Les joueurs rejoignent une salle avec un code. À chaque manche, chacun reçoit un modèle tendance (image, GIF ou vidéo courte) et doit trouver la meilleure légende avant la fin du chrono. Ensuite, tout le monde note les mèmes des autres et un classement s'affiche. À la fin de la partie, un podium désigne le roi du mème.

Le jeu s'inspire du principe des jeux de légendes de mèmes, mais il a son propre nom, son propre logo et son propre design.

## Lancer le jeu

Prérequis : le [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

- **Windows** : double-clique sur `lancer_jeu.cmd`. Le serveur démarre et le navigateur s'ouvre sur http://localhost:5180.
- **En ligne de commande** :

  ```bash
  dotnet run --project FaisTonMeme.Web
  ```

Pour tester à plusieurs sur un seul PC, ouvre **un onglet par joueur** : chaque onglet est un joueur différent.

Pour jouer avec des téléphones sur le même Wi-Fi, lance le serveur avec `dotnet run --project FaisTonMeme.Web --urls http://0.0.0.0:5180`, puis ouvre `http://<adresse-IP-du-PC>:5180` sur les téléphones. Le pare-feu Windows te demandera peut-être d'autoriser la connexion.

## Déroulement d'une partie

1. **Salon** : l'hôte crée la salle et partage le code ou le lien d'invitation. Il choisit le nombre de manches, les durées, le nombre de changements de modèle autorisés à chaque manche (jusqu'à 50) et les types de médias.
2. **Légende** : chaque joueur reçoit son propre modèle. Sur une image, les textes se placent en les faisant glisser. Sur un GIF ou une vidéo, la légende va dans un bandeau au-dessus. Le brouillon est enregistré au fil de la frappe : s'il n'est pas validé, il est pris tel quel à la fin du chrono.
3. **Vote** : les mèmes défilent un par un, sans le nom de leur auteur. On note *Bof* (1 point), *Drôle* (2) ou *Légendaire* (3). On ne peut pas voter pour soi.
4. **Résultats** : les auteurs sont révélés, les points sont ajoutés au classement, puis la manche suivante commence.
5. **Fin** : podium, classement final et meilleurs mèmes de la partie. L'hôte peut relancer une partie.

Si un joueur recharge la page ou perd la connexion, il retrouve sa place automatiquement. Si l'hôte part, un autre joueur prend le relais.

## D'où viennent les mèmes

| Source | Contenu | Clé | Mise à jour |
|---|---|---|---|
| [Imgflip](https://imgflip.com/api) | Les ~100 modèles d'images les plus utilisés en ce moment | Aucune | Tâche planifiée du serveur (toutes les 6 h par défaut) |
| [memegen.link](https://api.memegen.link) | ~180 modèles classiques vierges (projet open source), sans les doublons d'Imgflip | Aucune | Tâche planifiée du serveur |
| [Openverse](https://api.openverse.org) | Plus de 2 000 photos d'animaux drôles sous licence Creative Commons | Aucune | Une récolte par jour au plus (limite de 200 requêtes par jour sans compte) |
| [YouTube Data API](https://developers.google.com/youtube/v3) | Vidéos courtes récentes (7 derniers jours) sur les recherches configurées, triées par vues, SafeSearch strict | Gratuite (Google Cloud) | Tâche planifiée du serveur |
| [KLIPY](https://docs.klipy.com) | GIF et clips tendance, localisés en français | Gratuite ([partner.klipy.com](https://partner.klipy.com)) | Chargés en direct au début de chaque partie |
| Communauté | Modèles proposés par les joueurs sur `/proposer.html` | — | Après validation sur `/moderation.html` |

Sans aucune clé, le jeu compte déjà plus de 2 000 modèles (Imgflip, memegen.link, Openverse et propositions validées). YouTube et KLIPY s'activent dès qu'on ajoute leur clé. Le nombre exact de modèles est affiché sur la page `/bibliotheque.html`.

Les photos Openverse sont bien plus nombreuses que les modèles de mèmes classiques. Pour que les parties restent surtout faites de vrais mèmes, elles ne sortent que dans 35 % des tirages d'images.

### Pourquoi pas TikTok, Instagram ou X ?

Aucune de ces plateformes n'a d'API publique qui permette de récupérer leurs mèmes. Aspirer leurs pages enfreint leurs conditions d'utilisation, et les vidéos appartiennent à leurs auteurs. Fais ton mème utilise donc seulement des API officielles, plus des modèles proposés par les joueurs et modérés.

À noter également : Google a fermé l'API Tenor le 30 juin 2026, et Reddit ne prend plus de nouvelles demandes d'accès à son API publique après le 31 octobre 2026.

### Règles respectées pour chaque source

- **YouTube** : les vidéos sont lues avec le lecteur YouTube intégré (`youtube-nocookie.com`), jamais téléchargées. Rien n'est écrit par-dessus le lecteur : la légende est dans un bandeau au-dessus. Les données de l'API sont supprimées si une vidéo n'a pas été revue depuis 30 jours.
- **KLIPY** : leurs [exigences d'intégration](https://docs.klipy.com/integration-requirements) imposent que les appels partent du navigateur et que les médias soient chargés directement depuis leurs liens. Le navigateur de l'hôte récupère donc les tendances au lancement du salon, et le serveur garde seulement les liens en mémoire pour la partie. Rien n'est stocké ni recopié, et chaque GIF ou clip affiche « Powered by KLIPY ». La clé KLIPY est donc visible dans le navigateur : c'est prévu ainsi par KLIPY. Les filtres de contenu se règlent dans leur tableau de bord partenaire. Si tu veux un jour mettre KLIPY en cache côté serveur, il faut leur accord écrit (developers@klipy.com).
- **Imgflip** et **memegen.link** : les images sont affichées depuis leurs serveurs, avec un lien vers la page d'origine.
- **Openverse** : on ne demande que des licences qui autorisent la modification (ajouter une légende en est une), donc aucune licence « ND ». Le contenu adulte est exclu. L'auteur, la licence et le lien vers l'original sont affichés sous chaque photo. Les licences « NC » interdisent l'usage commercial : si le jeu devient un jour payant ou financé par la publicité, il faudra filtrer avec `license_type=commercial,modification`. Les recherches portent sur des animaux, pour éviter de légender des photos de personnes privées. Une photo gênante se retire depuis la page de modération.

## Configuration

Les réglages se trouvent dans `FaisTonMeme.Web/appsettings.json`. Les clés et le mot de passe vont dans `FaisTonMeme.Web/appsettings.Local.json`, ignoré par Git. Copie le fichier d'exemple :

```bash
copy FaisTonMeme.Web\appsettings.Local.example.json FaisTonMeme.Web\appsettings.Local.json
```

| Réglage | Rôle | Défaut |
|---|---|---|
| `FaisTonMeme:RefreshIntervalHours` | Intervalle entre deux mises à jour des sources | `6` |
| `FaisTonMeme:AdminPassword` | Mot de passe de la page de modération | généré au premier lancement |
| `FaisTonMeme:Imgflip:Enabled` | Active la source Imgflip | `true` |
| `FaisTonMeme:Memegen:Enabled` | Active la source memegen.link | `true` |
| `FaisTonMeme:Openverse:Enabled` | Active la source Openverse | `true` |
| `FaisTonMeme:Openverse:Queries` | Recherches Openverse (12 pages de 20 photos maximum par recherche) | 15 recherches d'animaux |
| `FaisTonMeme:Openverse:RefreshHours` | Délai minimum entre deux récoltes Openverse | `24` |
| `FaisTonMeme:YouTube:ApiKey` | Clé de l'API YouTube Data v3 | vide (source inactive) |
| `FaisTonMeme:YouTube:Queries` | Recherches lancées (100 unités de quota chacune, sur 10 000 par jour) | `meme #shorts`, `memes français #shorts` |
| `FaisTonMeme:YouTube:MaxAgeDays` | Âge maximum des vidéos | `7` |
| `FaisTonMeme:Klipy:ApiKey` | Clé KLIPY | vide (source inactive) |
| `FaisTonMeme:Klipy:Locale` / `ContentFilter` | Langue des tendances / niveau de filtre (`off`, `low`, `medium`, `high`) | `fr` / `high` |

Chaque réglage peut aussi être passé en variable d'environnement, par exemple `FaisTonMeme__YouTube__ApiKey`.

**Obtenir une clé YouTube** : dans la [Google Cloud Console](https://console.cloud.google.com/), crée un projet, active « YouTube Data API v3 », puis crée une clé API dans *Identifiants*.

## Modération

- `/proposer.html` : un joueur envoie une image, un GIF, une vidéo MP4/WebM (15 Mo maximum), ou un lien YouTube ou direct. Le vrai type du fichier est vérifié, et chaque adresse IP est limitée à 5 propositions par 10 minutes.
- `/moderation.html` : on valide ou refuse les propositions. On peut aussi retirer n'importe quel modèle de la bibliothèque : il ne reviendra pas, même si la source le propose à nouveau. Un bouton relance la mise à jour des sources sans attendre.
- Le mot de passe s'affiche dans la console au démarrage. Il est enregistré dans `FaisTonMeme.Web/data/mot-de-passe-moderation.txt`, sauf s'il est défini dans `AdminPassword`.
- Les fichiers en attente ne sont pas accessibles publiquement. Ils ne le deviennent qu'une fois validés.

## Architecture

```
FaisTonMeme.Web/
├── Program.cs              API minimale : config, bibliothèque, propositions, modération, hub SignalR
├── Game/
│   ├── Models.cs           Salle, joueur, réglages, légendes
│   ├── RoomManager.cs      Règles du jeu, chronomètres, choix des modèles, état envoyé à chaque joueur
│   └── GameHub.cs          Hub SignalR /hub/jeu et boucle des chronomètres
├── Library/
│   ├── LibraryStore.cs     Bibliothèque (data/library.json) et liste des modèles retirés
│   ├── ImgflipSource.cs    Source Imgflip
│   ├── MemegenSource.cs    Source memegen.link
│   ├── OpenverseSource.cs  Source Openverse (photos Creative Commons)
│   ├── YouTubeSource.cs    Source YouTube
│   ├── LibraryRefresher.cs Tâche planifiée de mise à jour
│   └── SubmissionStore.cs  Propositions des joueurs
├── Seed/templates.json     Modèles de départ (utilisés si la bibliothèque est vide)
└── wwwroot/                Front en JavaScript (modules ES), sans étape de build
    ├── index.html, js/app.js       Le jeu
    ├── js/meme.js                  Affichage d'un mème
    ├── js/klipy.js                 Tendances KLIPY (côté navigateur)
    ├── bibliotheque.html           Bibliothèque et état des sources
    ├── proposer.html               Proposer un modèle
    └── moderation.html             Modération
```

- **Temps réel** : ASP.NET Core SignalR. Le serveur fait autorité : il gère les chronomètres, choisit les modèles et compte les votes. Chaque joueur reçoit sa propre vue de l'état, ce qui garde secrets les auteurs pendant le vote.
- **Choix des modèles** : un type de média est d'abord tiré au hasard parmi ceux autorisés et disponibles, pour que les vidéos et les GIF sortent vraiment. Ensuite, un modèle est tiré en favorisant les nouveautés (moins de 7 jours) et le haut des classements. Un même modèle ne revient pas deux fois dans une partie, sauf si la bibliothèque est épuisée.
- **Données** : fichiers JSON dans `FaisTonMeme.Web/data/`. Les salles vivent en mémoire et disparaissent quand le serveur s'arrête.
- **Front** : HTML, CSS et JavaScript, sans framework ni build. Adapté au mobile, avec thème clair et sombre. Le client SignalR est fourni dans `wwwroot/lib`.
