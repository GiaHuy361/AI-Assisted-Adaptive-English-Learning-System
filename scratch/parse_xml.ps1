$xmlPath = "d:\hoctap\ki8\PRN232\AI-Assisted-Adaptive-English-Learning-System\scratch\extracted\word\document.xml"
$xmlText = [System.IO.File]::ReadAllText($xmlPath, [System.Text.Encoding]::UTF8)
[xml]$xml = $xmlText

$ns = New-Object Xml.XmlNamespaceManager $xml.NameTable
$ns.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
$paragraphs = $xml.SelectNodes("//w:p", $ns)
$textOutput = foreach ($p in $paragraphs) {
    $runs = $p.SelectNodes(".//w:t", $ns)
    if ($runs) {
        ($runs | ForEach-Object { $_.InnerText }) -join ""
    }
}
$textOutput | Out-File -FilePath "scratch/extracted_guide.txt" -Encoding utf8
Write-Host "Successfully parsed XML with UTF-8 encoding to scratch/extracted_guide.txt"
