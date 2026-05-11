using FluentAssertions;
using UrlShortener.Api.Core;

namespace UrlShortener.Tests.Unit;
public class Base62ConverterTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(62L)]
    [InlineData(1234567890123L)]
    public void Should_encode_and_decode(long value)
    {
        var encoded = Base62Converter.Encode(value);
        Base62Converter.Decode(encoded).Should().Be(value);
    }
}
