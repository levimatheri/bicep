var foo = 'abc'
var bar = guid(subscription().id, 'xxxx', foo)
//@[4:7) [no-unused-vars (Warning)] Variable "bar" is declared but never used. (bicep core linter https://aka.ms/bicep/linter/no-unused-vars) |bar|
var abc = guid('blah')
//@[4:7) [no-unused-vars (Warning)] Variable "abc" is declared but never used. (bicep core linter https://aka.ms/bicep/linter/no-unused-vars) |abc|
var def = {
//@[4:7) [no-unused-vars (Warning)] Variable "def" is declared but never used. (bicep core linter https://aka.ms/bicep/linter/no-unused-vars) |def|
  '1234': '1234'
//@[2:8) [prefer-unquoted-property-names (Warning)] Property names that are valid identifiers should be declared without quotation marks and accessed using dot notation. (bicep core linter https://aka.ms/bicep/linter/prefer-unquoted-property-names) |'1234'|
  '${guid('blah')}': guid('blah')
}

