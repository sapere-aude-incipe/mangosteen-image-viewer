namespace Mangosteen.Rendering.Advanced;

internal interface ILargeImageSource
{
    Task<LargeImageMetadata> LoadMetadataAsync(string path, CancellationToken token);
    Task<ImageTileData> DecodeTileAsync(string path, ImagePyramid pyramid, ImageTileKey key, CancellationToken token);
}
