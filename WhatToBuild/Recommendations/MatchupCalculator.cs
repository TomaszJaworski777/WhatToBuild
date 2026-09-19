using WhatToBuild.Data;
using WhatToBuild.Dtos;
using WhatToBuild.Game;
using WhatToBuild.SupportedChampions;

namespace WhatToBuild.Recommendations;

public sealed class MatchupCalculator
{
    private readonly NeutralRepository _neutrals;
    private readonly ChampionKits _kits;

    public MatchupCalculator(NeutralRepository neutrals, ChampionKits kits)
    {
        _neutrals = neutrals;
        _kits = kits;
    }

    public IReadOnlyList<MatchupDto> For(GameState state)
    {
        if (OurFights.For(state, _neutrals, _kits) is not { } fights)
        {
            return [];
        }

        return fights.Enemies
            .Select(e =>
            {
                var casts = fights.Hints(e.Entity)
                    .Select(h => new CastHintDto(
                        h.Ability,
                        h.CastAtHealth,
                        h.KillingHealth,
                        h.Additions.Select(a => $"+{a.Damage:0} {a.When}").ToList()))
                    .ToList();

                return new MatchupDto(e.Player.Champion.Name, null, 0, casts);
            })
            .ToList();
    }
}
