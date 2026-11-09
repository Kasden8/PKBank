using FluentAssertions;
using Xunit;

namespace PKHeX.Core.Tests.Legality;

public class LegalityData
{
    [Theory]
    [InlineData(Species.Feebas, 0)] // Feebas, see issue #2394
    [InlineData(Species.Crabrawler, 0)] // S/V Crabrawler added a second, UseItem evolution method. Need to be sure it's before the more restrictive level-up method.
    public void EvolutionsOrdered(Species species, byte form)
    {
        int count = 0;
        for (var context = EntityContext.None + 1; context < EntityContext.MaxInvalid; context++)
        {
            if (!context.IsValid)
                continue;
            if (context is EntityContext.Gen8bLumi)
                continue; // ---- PKBank fork (fork PKForge-PKHeX): BDSP variant with its own evolution table; ordering rules checked elsewhere (see LuminescentPlatinumTests in Engine.Tests).

            var tree = EvolutionTree.GetEvolutionTree(context);
            var possible = tree.Forward.GetForward((ushort)species, form).Span;
            if (possible.Length <= 1)
                continue;

            var t1 = possible[0].Method;
            var t2 = possible[1].Method;

            t1.IsLevelUpRequired.Should().BeFalse();
            t2.IsLevelUpRequired.Should().BeTrue();

            count++;
        }

        count.Should().BeGreaterThan(0);
    }
}
