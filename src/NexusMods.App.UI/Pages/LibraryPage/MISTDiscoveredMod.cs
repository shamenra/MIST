// MIST-006-14B2B5: display-only discovery candidate, NEVER a managed LibraryItem.
namespace NexusMods.App.UI.Pages.LibraryPage;

public sealed record MISTDiscoveredMod(
    string GroupKey,
    string Name,
    string FileCountText,
    string VersionText,
    string RecognitionText,
    string FilePathsText,
    string TotalSizeText,
    long TotalSizeBytes);
