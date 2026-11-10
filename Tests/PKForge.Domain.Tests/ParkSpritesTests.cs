using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>The park shows each form's own PMD sprite (PKHeX and PMDCollab number forms
/// differently), and the still sprite where PMDCollab has not drawn that form or that shiny.</summary>
public sealed class ParkSpritesTests
{
    [Theory]
    [InlineData(25, 0, false, "0025")]
    [InlineData(25, 0, true, "0025/0000/0001")]
    [InlineData(26, 1, false, "0026/0001")]          // Alolan Raichu
    [InlineData(26, 1, true, "0026/0001/0001")]
    [InlineData(59, 1, false, "0059/0001")]          // Hisuian Arcanine
    [InlineData(201, 1, false, "0201/0001")]         // Unown B
    [InlineData(666, 0, false, "0666/0001")]         // Vivillon: PKHeX's form 0 is Icy Snow, PMD's base is Meadow
    [InlineData(666, 6, false, "0666")]              // Meadow
    [InlineData(774, 3, false, "0774")]              // Minior in its shell looks the same whatever its core
    [InlineData(774, 7, false, "0774/0001")]         // its red core
    [InlineData(869, 1, false, "0869/0007")]         // Ruby Cream Alcremie
    [InlineData(902, 1, false, "0902/0000/0000/0002")] // female Basculegion
    [InlineData(664, 5, false, "0664")]              // Scatterbug's pattern does not show
    public void Each_form_finds_its_own_sprite(int species, int form, bool shiny, string key) =>
        Assert.Equal(key, ParkSprites.Key(species, form, shiny));

    [Theory]
    [InlineData(25, 1, false)]  // Pikachu in Ash's original cap: not drawn
    [InlineData(774, 7, true)]  // no shiny core sprite
    [InlineData(0, 0, false)]
    public void Without_a_drawing_it_keeps_the_still_sprite(int species, int form, bool shiny) =>
        Assert.Null(ParkSprites.Key(species, form, shiny));
}
