# GlassDock

Une barre des tâches pour Windows 11, avec une apparence inspirée du Liquid Glass et des dossiers inspirés de l’iPhone.

![Aperçu de GlassDock](GlassDock-apercu.png)

## Fonctionnalités

- Remplacement de la barre des tâches Windows et adaptation au thème clair ou sombre du système.
- Réglage de l’opacité, de la largeur et de l’alignement des applications.
- Carrousel pour parcourir les icônes lorsque la largeur disponible est insuffisante.
- Épinglage d’applications par glisser-déposer et réorganisation des icônes.
- Création de dossiers en déposant une application sur une autre, avec une grille de neuf icônes par page.
- Renommage des dossiers en cliquant sur leur titre et création automatique de pages supplémentaires.
- Indicateurs des applications ouvertes et aperçus des fenêtres au survol.
- Accès aux icônes cachées, aux réglages rapides et au calendrier natifs de Windows.
- Masquage pendant les applications en plein écran, avec accès par la touche Windows.
- Sauvegarde des dossiers et des réglages, et option de démarrage automatique avec Windows.

## Installation

1. Télécharger le ZIP de la dernière version depuis la section **Releases** du dépôt.
2. Extraire **tous les fichiers** dans un même dossier.
3. Installer **.NET Desktop Runtime 10** si nécessaire.
4. Lancer `GlassDock.exe`.

Le fichier `.exe` doit rester à côté des DLL et des fichiers JSON fournis dans l’archive.

## Utilisation

- **Clic sur une application** : ouvrir l’application ou activer sa fenêtre.
- **Survol d’une application ouverte** : afficher un aperçu de sa fenêtre.
- **Glisser une icône entre deux applications** : modifier son emplacement.
- **Glisser une icône sur une autre** : créer un dossier.
- **Clic sur un dossier** : afficher ses applications. Cliquer sur le titre pour le renommer.
- **Clic droit sur une application** : ouvrir, épingler ou désépingler, terminer la tâche ou fermer la fenêtre.
- **Clic droit sur le fond de la barre** : ouvrir les paramètres ou le Gestionnaire des tâches.

Pour fermer GlassDock et rétablir la barre Windows, utiliser **Quitter et rétablir la barre Windows** dans les paramètres.

## Compiler le projet

Prérequis : **Windows** et **SDK .NET 10**.

Depuis le dossier contenant `GlassDock.csproj` :

```powershell
dotnet publish GlassDock.csproj -c Release -o dist
```

L’application compilée se trouve dans `dist`. Pour préparer une release, compresser tout son contenu dans un ZIP ; le fichier de débogage `GlassDock.pdb` peut être exclu.

Le dépôt contient les fichiers source `.cs`, `.xaml` et `GlassDock.csproj`. Les dossiers de compilation `bin`, `obj` et `dist` ne sont pas nécessaires dans GitHub.

## Données locales

Les réglages, dossiers et icônes mises en cache sont conservés dans :

```text
%LOCALAPPDATA%\GlassDock
```

L’intégration de la barre Windows est prévue pour l’écran principal. L’apparence du verre est simulée.

## Licence

GlassDock est distribué sous [licence MIT](LICENSE). L’utilisation, la modification et la redistribution sont autorisées, y compris commercialement, à condition de conserver la licence.
