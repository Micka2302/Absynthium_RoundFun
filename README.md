# Absynthium_RoundFun

Plugin CounterStrikeSharp pour rounds fun aleatoires sur serveur AWP CS2.

## Rounds

- `NOSCOPE AWP`: tous les joueurs jouent AWP sans scope.
- `SSG`: tous les joueurs jouent SSG08.
- `SSG NOSCOPE`: tous les joueurs jouent SSG08 sans scope.

Au debut du round pendant le freezetime, le plugin annonce:

```text
ROUND FUN - <TYPE>
```

L'annonce HTML utilise les images configurees dans `Absynthium_RoundFun/lang/en.json`:

- `round.announce.image.noscope_awp`
- `round.announce.image.ssg`
- `round.announce.image.ssg_noscope`

## Commandes

- `!noscopawp`: force le round fun NOSCOPE AWP au prochain round.
- `!ssg`: force le round fun SSG au prochain round.
- `!noscopssg`: force le round fun SSG NOSCOPE au prochain round.

Alias aussi disponibles: `!noscopeawp`, `!noscopessg`.

## Config

CounterStrikeSharp genere la config du plugin au chargement. Un exemple est disponible dans:

```text
Absynthium_RoundFun/config.example.json
```

Champs principaux:

- `FunRoundChancePercent`: chance qu'un round fun aleatoire parte quand aucun round n'est force.
- `EnableNoscopAwp`: active ou desactive les rounds NOSCOPE AWP.
- `EnableSsg`: active ou desactive les rounds SSG.
- `EnableSsgNoscop`: active ou desactive les rounds SSG NOSCOPE.
- `ForceCommandPermission`: permission requise pour forcer un round, vide pour autoriser tout le monde.

## Compilation

Depuis `Absynthium_RoundFun/Absynthium_RoundFun`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\compile.ps1
```

Sorties:

- `compiled/counterstrikesharp/plugins/Absynthium_RoundFun`
- `compiled/Absynthium_RoundFun.zip`
