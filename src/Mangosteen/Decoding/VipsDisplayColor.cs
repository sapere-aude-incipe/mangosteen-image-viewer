using NetVips;

namespace Mangosteen.Decoding;

internal static class VipsDisplayColor
{
    public static Image ConvertToSrgb(Image image, bool preserveHighBitDepth)
    {
        var highBitDepth = preserveHighBitDepth && image.Format is not (Enums.BandFormat.Uchar or Enums.BandFormat.Char);
        if (image.GetTypeOf("icc-profile-data") != 0)
        {
            try
            {
                return image.IccTransform("srgb", intent: Enums.Intent.Relative,
                    blackPointCompensation: true, embedded: true, depth: highBitDepth ? 16 : 8);
            }
            catch (VipsException)
            {
                // An unusable embedded profile must not prevent displaying the image.
            }
        }

        var target = highBitDepth ? Enums.Interpretation.Rgb16 : Enums.Interpretation.Srgb;
        return image.Interpretation == target ||
            (highBitDepth && image.Interpretation == Enums.Interpretation.Srgb && image.Format == Enums.BandFormat.Ushort)
            ? image.Copy()
            : image.Colourspace(target);
    }
}
