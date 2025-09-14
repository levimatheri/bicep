// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Syntax;

namespace Bicep.Core.Parsing;

public class ReplParser : BaseParser
{
    public ReplParser(string text) : base(text)
    {
    }

    // public override ProgramSyntax Program()
    // {
    //     var declarationsExpressionsOrTokens = new List<SyntaxBase>();

    //     while (!this.IsAtEnd())
    //     {
    //         // this produces either a declaration node, skipped tokens node or just a token
    //         var declarationExpressionOrToken = Declaration();
    //         declarationsExpressionsOrTokens.Add(declarationExpressionOrToken);

    //         // if skipped node is returned above, the newline is not consumed
    //         // if newline token is returned, we must not expect another (could be a beginning of a declaration)
    //         if (declarationExpressionOrToken is StatementSyntax)
    //         {
    //             // declarations must be followed by a newline or the file must end
    //             var newLine = this.WithRecoveryNullable(this.NewLineOrEof, RecoveryFlags.ConsumeTerminator, TokenType.NewLine);
    //             if (newLine != null)
    //             {
    //                 declarationsExpressionsOrTokens.Add(newLine);
    //             }
    //         }
    //     }

    //     var endOfFile = reader.Read();
    //     var programSyntax = new ProgramSyntax(declarationsExpressionsOrTokens, endOfFile);

    //     var parsingErrorVisitor = new ParseDiagnosticsVisitor(this.ParsingErrorTree);
    //     parsingErrorVisitor.Visit(programSyntax);

    //     return programSyntax;
    // }

    public SyntaxBase Parse() => this.Declaration();

    public override ProgramSyntax Program()
    {
        throw new NotImplementedException();
    }

    protected override SyntaxBase Declaration(params string[] expectedKeywords) =>
        this.WithRecovery(
            () =>
            {
                var current = reader.Peek();

                return current.Type switch
                {
                    TokenType.Identifier => ValidateKeyword(current.Text) switch
                    {
                        LanguageConstants.VariableKeyword => this.VariableDeclaration([]),
                        _ => this.TryParseExpression(),
                    },
                    TokenType.NewLine => this.NewLine(),
                    _ => this.TryParseExpression(),
                };

                string? ValidateKeyword(string keyword) =>
                    expectedKeywords.Length == 0 || expectedKeywords.Contains(keyword) ? keyword : null;
            },
            RecoveryFlags.None,
            TokenType.NewLine);

    private SyntaxBase TryParseExpression() => this.Expression(ExpressionFlags.AllowComplexLiterals);
}
