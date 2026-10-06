# GlassDock

Une barre des tâches pour Windows 11, inspirée du Liquid Glass.

![GlassDock](GlassDock-apercu.png)

## Fonctionnalités

- Apparence adaptée au thème Windows, opacité et largeur réglables.
- Applications et dossiers réorganisables par glisser-déposer.
- Dossiers avec une grille 3 × 3 et plusieurs pages.
- Aperçus des fenêtres et indicateurs des applications ouvertes.
- Accès aux panneaux natifs de Windows : calendrier, réglages rapides et icônes cachées.
- Masquage en plein écran et démarrage automatique en option.

## Installation

1. Télécharger le ZIP depuis **Releases**.
2. Extraire tous les fichiers dans un même dossier.
3. Lancer `GlassDock.exe`.

**Prérequis :** Windows 11 et .NET Desktop Runtime 10.

## Utilisation

Glisser une application sur une autre pour créer un dossier. Cliquer sur le titre du dossier pour le renommer.

Faire un clic droit sur la barre pour accéder aux paramètres ou au Gestionnaire des tâches. Pour quitter, utiliser le bouton prévu dans les paramètres : la barre Windows est rétablie.

## Compilation

Avec le SDK .NET 10 sous Windows :

```powershell
dotnet publish GlassDock.csproj -c Release -o dist
```

Les fichiers prêts à lancer sont générés dans `dist`.

## Licence

[MIT](LICENSE) — utilisation, modification et redistribution autorisées.
