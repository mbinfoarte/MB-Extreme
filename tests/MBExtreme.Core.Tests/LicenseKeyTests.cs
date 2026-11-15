using MBExtreme.Core.Licensing;
using Xunit;

namespace MBExtreme.Core.Tests;

public class LicenseKeyTests
{
    [Theory]
    [InlineData("MBXT-ABCD-EFGH-JKLM-NP23", "MBXT-ABCD-EFGH-JKLM-NP23")]
    [InlineData(" mbxt abcd efgh jklm np23 ", "MBXT-ABCD-EFGH-JKLM-NP23")]
    [InlineData("MBXTABCDEFGHJKLMNP23", "MBXT-ABCD-EFGH-JKLM-NP23")]
    [InlineData("mbxt-abcdefgh-jklm-np23", "MBXT-ABCD-EFGH-JKLM-NP23")]
    [InlineData("-OUTRA-CHAVE-", "OUTRA-CHAVE")]
    public void NormalizeKey_PutsDashesBack(string input, string expected)
        => Assert.Equal(expected, LicenseClient.NormalizeKey(input));
}
