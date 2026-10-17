namespace NeoShell.Widgets;

/// <summary>
/// The picture puzzle's board: fifteen tiles and a gap in four rows of four. A tile next to the gap slides into it;
/// solved, the tiles are in order with the gap last.
/// </summary>
public sealed class SlidingPuzzle
{
    public const int Size = 4;

    // What is at each place, row by row: tile 1 to 15, 0 for the gap.
    private readonly int[] _places = new int[Size * Size];

    public SlidingPuzzle()
    {
        for (int i = 0; i < _places.Length - 1; i++)
            _places[i] = i + 1;
    }

    /// <summary>For tests: the board with these tiles at the places, 0 for the gap.</summary>
    public SlidingPuzzle(IReadOnlyList<int> places)
    {
        if (places.Count != _places.Length || !places.Order().SequenceEqual(Enumerable.Range(0, _places.Length)))
            throw new ArgumentException("Every tile and one gap, once each.", nameof(places));
        _places = [.. places];
    }

    public int Gap => Array.IndexOf(_places, 0);

    public bool IsSolved => _places.Take(_places.Length - 1).Select((tile, place) => tile == place + 1).All(right => right);

    /// <summary>Where a tile (1 to 15) is.</summary>
    public int PlaceOf(int tile) => Array.IndexOf(_places, tile);

    /// <summary>A tile can move when it's beside the gap, in the same row or column.</summary>
    public bool CanMove(int tile)
    {
        int place = PlaceOf(tile), gap = Gap;
        return place >= 0 && Math.Abs(place / Size - gap / Size) + Math.Abs(place % Size - gap % Size) == 1;
    }

    /// <summary>Slides a tile into the gap; false when it isn't beside it.</summary>
    public bool Move(int tile)
    {
        if (tile == 0 || !CanMove(tile))
            return false;
        int place = PlaceOf(tile);
        _places[Gap] = tile;
        _places[place] = 0;
        return true;
    }

    /// <summary>
    /// Mixes the tiles by sliding them at random, so the board can always be solved (half of all orders can't), and
    /// never leaves it solved.
    /// </summary>
    public void Shuffle(Random random)
    {
        int last = 0;
        do
        {
            for (int i = 0; i < 200; i++)
            {
                // Not straight back: that would undo the move before.
                int[] movable = [.. _places.Where(tile => tile != 0 && tile != last && CanMove(tile))];
                last = movable[random.Next(movable.Length)];
                Move(last);
            }
        }
        while (IsSolved);
    }
}
