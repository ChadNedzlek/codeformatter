using Xunit;

namespace Microsoft.DotNet.CodeFormatting.Tests
{
    public sealed class UsingOrderRuleTests : SyntaxRuleTestBase
    {
        internal override ISyntaxFormattingRule Rule => new Rules.UsingOrderRule();

        [Fact]
        public void LeaveAloneFineThings()
        {
            var source = @"using A;
using B;
namespace NS
{
    class C1 { }
}";

            Verify(source, source);

        }

        [Fact]
        public void FlipUsingNamespace()
        {
            var source = @"using B;
using A;
namespace NS
{
    class C1 { }
}";

            var expected = @"using A;
using B;
namespace NS
{
    class C1 { }
}";

            Verify(source, expected);

        }

        [Fact]
        public void FlipAliases()
        {
            var source = @"using B = Y;
using A = Z;
namespace NS
{
    class C1 { }
}";

            var expected = @"using A = Z;
using B = Y;
namespace NS
{
    class C1 { }
}";

            Verify(source, expected);

        }

        [Fact]
        public void FlipStatic()
        {
            var source = @"using static B;
using static A;
namespace NS
{
    class C1 { }
}";

            var expected = @"using static A;
using static B;
namespace NS
{
    class C1 { }
}";

            Verify(source, expected);

        }

        [Fact]
        public void AddSpaces()
        {
            var source = @"using A;
using B = C;
using static D;
namespace NS
{
    class C1 { }
}";

            var expected = @"using A;

using B = C;

using static D;
namespace NS
{
    class C1 { }
}";

            Verify(source, expected);

        }

        [Fact]
        public void PutItAllTogether()
        {
            var source = @"using static D;
using Z;
using E = C;
using B = Foo;
using A;
namespace NS
{
    class C1 { }
}";

            var expected = @"using A;
using Z;

using B = Foo;
using E = C;

using static D;
namespace NS
{
    class C1 { }
}";

            Verify(source, expected);

        }
    }
}