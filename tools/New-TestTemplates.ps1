<#
.SYNOPSIS
  Assembles five test templates in out\test-templates from Office's own templates and themes.
  (Office's files aren't ours to redistribute, so they're built locally rather than committed.)
    office-16x9.potx   Office's WidescreenPresentation.potx
    pitchbook.potx     Office's Pitchbook.potx (a designed, corporate-style template)
    deck-4x3.pptx      4:3 deck with existing slides, two sections and a custom show
    multi-master.pptx  two masters (Office Theme + Ion), slides on each
    dark.pptx          Office Theme with a dark background colour map
#>
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\out\test-templates'
New-Item -ItemType Directory -Force $out | Out-Null
$out = Resolve-Path $out
$tpl = 'C:\Program Files\Microsoft Office\root\Templates\1033'
$themes = 'C:\Program Files\Microsoft Office\root\Document Themes 16'
Copy-Item "$tpl\WidescreenPresentation.potx" "$out\office-16x9.potx" -Force
Copy-Item "$tpl\Pitchbook.potx" "$out\pitchbook.potx" -Force

$ppt = New-Object -ComObject PowerPoint.Application
try {
    # 4:3 deck with slides, sections and a custom show
    $p = $ppt.Presentations.Add(0)
    $p.PageSetup.SlideSize = 2          # ppSlideSizeOnScreen (4:3)
    $l = $p.SlideMaster.CustomLayouts
    $s1 = $p.Slides.AddSlide(1, $l.Item(1)); $s1.Shapes.Item(1).TextFrame.TextRange.Text = 'Old title slide'
    $s2 = $p.Slides.AddSlide(2, $l.Item(2)); $s2.Shapes.Item(1).TextFrame.TextRange.Text = 'Old content'
    $null = $p.SectionProperties.AddBeforeSlide(1, 'Intro')
    $null = $p.SectionProperties.AddBeforeSlide(2, 'Body')
    $null = $p.SlideShowSettings.NamedSlideShows.Add('Short', [int[]]@($s1.SlideID))
    $p.SaveAs("$out\deck-4x3.pptx"); $p.Close()

    # Two masters
    $p = $ppt.Presentations.Add(0)
    $null = $p.Slides.AddSlide(1, $p.SlideMaster.CustomLayouts.Item(2))
    $d = $p.Designs.Load("$themes\Ion.thmx")
    $null = $p.Slides.AddSlide(2, $d.SlideMaster.CustomLayouts.Item(2))
    $p.SaveAs("$out\multi-master.pptx"); $p.Close()

    # Dark: Office Theme with the dark background style (bg1 = dk1)
    $p = $ppt.Presentations.Add(0)
    $null = $p.Slides.AddSlide(1, $p.SlideMaster.CustomLayouts.Item(6))
    $p.SlideMaster.BackgroundStyle = 12  # msoBackgroundStylePreset12: dark
    $p.SaveAs("$out\dark.pptx"); $p.Close()
}
finally { $ppt.Quit() }
Get-ChildItem $out | Select-Object Name, Length
