namespace o_down.Core.Models;

public enum MediaFormatPreference
{
    Best = 0,
    Worst = 1,
    BestVideoPlusBestAudio = 2,
    BestVideoOnly = 3,
    BestAudioOnly = 4,
    Smallest = 5,
    Largest = 6,
    Custom = 7
}
