using LabelsMis.Domain.Estimating.Models;

namespace LabelsMis.Domain.Estimating.Calculations;

internal static class IndigoInkSeparations
{
    public static int ColorSeparationsPerFrame(IndigoInkSet inkSet) =>
        inkSet switch
        {
            IndigoInkSet.EPM => 3,
            IndigoInkSet.EPMW => 3,
            IndigoInkSet.CMYK => 4,
            IndigoInkSet.CMYK_PlusSpot => 4,
            IndigoInkSet.CMYKW => 4,
            IndigoInkSet.CMYKW_PlusSpot => 4,
            _ => 4
        };
}
