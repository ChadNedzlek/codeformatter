// // Copyright (c) Microsoft. All rights reserved.
// // Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Microsoft.DotNet.CodeFormatting.Tests
{
    /// <summary>
    /// Test that fields are correctly identified as readonly.
    /// </summary>
    public class MarkReadonlyFieldTests : GlobalSemanticRuleTestBase
    {
        internal override IGlobalSemanticFormattingRule Rule
        {
            get { return new Rules.MarkReadonlyFieldsRule(); }
        }

        // In general a single sting with "READONLY" in it is used
        // for the tests to simplify the before/after comparison
        // The Original method will remove it, and the Readonly will replace it
        // with the keyword

        [Fact]
        public void TestIgnoreExistingReadonlyField()
        {
            var text = @"
class C
{
    private readonly int alreadyFine;
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestMarkReadonlyWithNoReferences()
        {
            var text = @"
class C
{
    private READONLY int read;
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestMarkReadonlyInternalWithNoReferences()
        {
            var text = @"
class C
{
    internal READONLY int read;
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestIgnoredReadonlyInternalWithNoReferencesByInternalsVisibleTo()
        {
            var text = @"
[assembly: System.Runtime.CompilerServices.InternalsVisibleToAttribute(""Some.Other.Assembly"")]
class C
{
    internal int exposed;
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestIgnoredPublic()
        {
            var text = @"
public class C
{
    public int exposed;
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestMarkedPublicInInternalClass()
        {
            var text = @"
internal class C
{
    public READONLY int notExposed;
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestIgnoredReadonlyWithWriteReferences()
        {
            var text = @"
class C
{
    private int wrote;

    public void T()
    {
        wrote = 5;
    }
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestIgnoredReadonlyWithCompoundWriteReferences()
        {
            var text = @"
class C
{
    private int wrote;

    public void T()
    {
        wrote += 5;
    }
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestMarkReadonlyWithReadReferences()
        {
            var text = @"
class C
{
    private READONLY int read;
    private int writen;

    public void T()
    {
        int x = change;
        x = read;
        writen = read;
        X(read);
    }

    public void X(int a)
    {
    }
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestIgnoredReadonlyWithRefArgument()
        {
            var text = @"
class C
{
    private int read;

    public void M(ref int a)
    {
    }

    public void T()
    {
        M(ref read);
    }
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestIgnoredReadonlyWithOutArgument()
        {
            var text = @"
class C
{
    private int read;

    public void N(out int a)
    {
    }

    public void T()
    {
        N(out read);
    }
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestIgnoredReadonlyWithExternRefArgument()
        {
            var text = @"
class C
{
    private int read;

    private extern void M(ref C c);
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestMarkReadonlyWithWriteReferencesInConstructor()
        {
            var text = @"
class C
{
    private READONLY int read;

    public C()
    {
        read = 5;
        M(ref read);
        N(out read);
    }

    public void M(ref int a)
    {
    }

    public void N(out int a)
    {
    }
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestMultipleFiles()
        {
            string[] text =
            {
                @"
class C1
{
    internal READONLY int read;
    internal int wrote;

    public void M(C2 c)
    {
        c.wrote = 5;
        int x = c.read;
    }
}
",
                @"
class C2
{
    internal READONLY int read;
    internal int wrote;

    public void M(C1 c)
    {
        c.wrote = 5;
        int x = c.read;
    }
}
"
            };
            Verify(Original(text), Readonly(text), true, LanguageNames.CSharp);
        }

        private static string Original(string text)
        {
            return text.Replace("READONLY ", "");
        }

        private static string Readonly(string text)
        {
            return text.Replace("READONLY ", "readonly ");
        }

        private static string[] Original(string[] text)
        {
            return text.Select(Original).ToArray();
        }

        private static string[] Readonly(string[] text)
        {
            return text.Select(Readonly).ToArray();
        }
    }
}
