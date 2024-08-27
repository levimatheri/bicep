var unquoted = {
//@[4:12) [no-unused-vars (Warning)] Variable "unquoted" is declared but never used. (bicep core linter https://aka.ms/bicep/linter/no-unused-vars) |unquoted|
  _artifactsLocation: 123
  _artifactsLocationSasToken: 456
}
var stillQuoted = {
//@[4:15) [no-unused-vars (Warning)] Variable "stillQuoted" is declared but never used. (bicep core linter https://aka.ms/bicep/linter/no-unused-vars) |stillQuoted|
  '123': 123
//@[2:07) [prefer-unquoted-property-names (Warning)] Property names that are valid identifiers should be declared without quotation marks and accessed using dot notation. (bicep core linter https://aka.ms/bicep/linter/prefer-unquoted-property-names) |'123'|
  '+abc': 456
//@[2:08) [prefer-unquoted-property-names (Warning)] Property names that are valid identifiers should be declared without quotation marks and accessed using dot notation. (bicep core linter https://aka.ms/bicep/linter/prefer-unquoted-property-names) |'+abc'|
  '': 789
//@[2:04) [prefer-unquoted-property-names (Warning)] Property names that are valid identifiers should be declared without quotation marks and accessed using dot notation. (bicep core linter https://aka.ms/bicep/linter/prefer-unquoted-property-names) |''|
}

