$ErrorActionPreference = 'Stop'
$key = Join-Path $env:RUNNER_TEMP 'callrift-fixture-signing'
& ssh-keygen -q -t ed25519 -N '' -f $key
if ($LASTEXITCODE -ne 0) { throw 'Fixture signing key creation failed.' }
git config --global user.name 'callrift CI fixtures'
git config --global user.email 'fixtures@callrift.invalid'
git config --global gpg.format ssh
git config --global user.signingkey $key
git config --global commit.gpgsign true
git config --global core.autocrlf false
