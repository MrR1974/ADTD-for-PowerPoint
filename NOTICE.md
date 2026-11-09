# Notices

## ADTD.Net

ADTD for PowerPoint is a rebuild, for PowerPoint, of **ADTD.Net** (Active Directory Topology Diagrammer) by
Goran Mihelcic. Its LDAP queries, trust decoding and the WebLayout site-map geometry are ported from ADTD.Net's
source. ADTD.Net is published under the MIT License:

> MIT License
>
> Copyright (c) [year] [fullname]
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
> documentation files (the "Software"), to deal in the Software without restriction, including without limitation
> the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
> permit persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of
> the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO
> THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
> TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

(The copyright line is reproduced as it appears in ADTD.Net's LICENSE.txt.)

## Third-party components

All under the MIT License:

| Component | Used for |
|---|---|
| DocumentFormat.OpenXml (Microsoft) | Reading templates and writing .pptx files |
| SkiaSharp, HarfBuzzSharp (Microsoft / Xamarin) | Measuring text in the theme fonts; drawing the icon fallback |
| Svg.Skia (Wiesław Šoltés) | Rendering the SVG icon |
| WPF-UI (lepo.co) | The Fluent window and controls |
| CommunityToolkit.Mvvm (.NET Foundation) | View models |
| Microsoft.Extensions.DependencyInjection (Microsoft) | Wiring the app together |
| System.DirectoryServices.Protocols (Microsoft) | Reading Active Directory over LDAP |
