$ErrorActionPreference = 'Stop'
# Temporary fixture repositories are throwaway test data. Do not add Git signing or signing-key setup here.
git config --global user.name 'callrift CI fixtures'
git config --global user.email 'fixtures@callrift.invalid'
git config --global core.autocrlf false
