Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile("Assets/logo.png")
$ms = New-Object System.IO.MemoryStream
$img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$bytes = $ms.ToArray()
$fs = New-Object System.IO.FileStream("Assets/logo.ico", [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([int16]0)
$bw.Write([int16]1)
$bw.Write([int16]1)
$w = $img.Width; if ($w -ge 256) { $w = 0 }
$h = $img.Height; if ($h -ge 256) { $h = 0 }
$bw.Write([byte]$w)
$bw.Write([byte]$h)
$bw.Write([byte]0)
$bw.Write([byte]0)
$bw.Write([int16]0)
$bw.Write([int16]32)
$bw.Write([int]$bytes.Length)
$bw.Write([int]22)
$bw.Write($bytes)
$bw.Close()
$fs.Close()
