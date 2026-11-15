# Runs the whole test suite one project at a time (a 2-core build machine starves timing-based tests when two
# test programs run side by side) and tries again when it fails: a failed attempt is reported as annotations
# (one per failed test, readable without opening the raw log) so that a flaky test can be found and fixed.
param([int]$Attempts = 3)

for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
    $dir = "results$attempt"
    dotnet test MasterHub.slnx --nologo -c Release --no-build -m:1 --logger "trx" --results-directory $dir
    if ($LASTEXITCODE -eq 0) {
        if ($attempt -gt 1) { Write-Output "::warning title=Tests passed on attempt $attempt::an earlier attempt failed, see the annotations above" }
        exit 0
    }

    foreach ($f in Get-ChildItem $dir -Filter *.trx -ErrorAction SilentlyContinue) {
        [xml]$x = Get-Content $f.FullName
        $ns = New-Object Xml.XmlNamespaceManager($x.NameTable)
        $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        foreach ($r in $x.SelectNodes("//t:UnitTestResult[@outcome='Failed']", $ns)) {
            $msg = ($r.Output.ErrorInfo.Message + ' ' + $r.Output.ErrorInfo.StackTrace)
            if ($msg.Length -gt 900) { $msg = $msg.Substring(0, 900) }
            $msg = $msg -replace "`r", '' -replace "`n", ' | '
            Write-Output "::warning title=Attempt ${attempt}: $($r.testName)::$msg"
        }
    }
}
Write-Output "::error::Tests failed in all $Attempts attempts"
exit 1
