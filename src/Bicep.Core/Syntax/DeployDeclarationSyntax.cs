// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bicep.Core.Navigation;
using Bicep.Core.Parsing;
using Bicep.Core.Registry;

namespace Bicep.Core.Syntax
{
    public class DeployDeclarationSyntax : StatementSyntax, ITopLevelDeclarationSyntax, IArtifactReferenceSyntax
    {
        public DeployDeclarationSyntax(IEnumerable<SyntaxBase> leadingNodes, Token keyword, IdentifierSyntax name, SyntaxBase path, SyntaxBase body)
            : base(leadingNodes)
        {
            AssertKeyword(keyword, nameof(keyword), LanguageConstants.DeployKeyword);
            AssertSyntaxType(name, nameof(name), typeof(IdentifierSyntax));
            AssertSyntaxType(path, nameof(path), typeof(StringSyntax), typeof(SkippedTriviaSyntax));
            AssertSyntaxType(body, nameof(body), typeof(ObjectSyntax), typeof(SkippedTriviaSyntax));

            this.Keyword = keyword;
            this.Name = name;
            this.Path = path;
            this.Body = body;
        }

        public Token Keyword { get; }

        public IdentifierSyntax Name { get; }

        public SyntaxBase Path { get; }

        public SyntaxBase Body { get; }

        public SyntaxBase SourceSyntax => this.Path;

        public override TextSpan Span => TextSpan.Between(this.Keyword, this.Body);

        public override void Accept(ISyntaxVisitor visitor) => visitor.VisitDeployDeclarationSyntax(this);

        public ArtifactType GetArtifactType() => ArtifactType.Module;

        public ObjectSyntax? TryGetBody() =>
            this.Body switch
            {
                ObjectSyntax @object => @object,
                // blocked by assert in the constructor
                _ => throw new NotImplementedException($"Unexpected type of deploy value '{this.Body.GetType().Name}'.")
            };

        public ObjectSyntax GetBody() =>
            this.TryGetBody() ?? throw new InvalidOperationException($"A valid deploy body is not available on this deploy due to errors. Use {nameof(TryGetBody)}() instead.");

    }
}
