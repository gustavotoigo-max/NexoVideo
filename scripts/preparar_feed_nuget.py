"""Prepara feed local via HTTPS quando o NuGet/.NET falha no TLS do ambiente.

Não desativa certificados. Lê dependências .NET compatíveis dos nuspec e baixa
pacotes oficiais para .research/nuget-feed. Restaurar com --source nessa pasta.
"""
import io
import pathlib
import re
import shutil
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
feed = root / ".research/nuget-feed"
feed.mkdir(parents=True, exist_ok=True)
pending = []
for project in root.glob("**/*.csproj"):
    if ".packages" in project.parts:
        continue
    for reference in ET.parse(project).iter("PackageReference"):
        pending.append((reference.attrib["Include"].lower(), reference.attrib["Version"]))
visited = set()


def rank(framework):
    framework = framework.lower()
    if any(platform in framework for platform in ("ios", "android", "tvos", "maccatalyst", "macos")):
        return -2
    if not framework:
        return 1
    if ".netstandard" in framework or "netstandard" in framework:
        return 20
    if ".netcoreapp" in framework or re.match(r"net[5-9]\.\d|net10\.\d", framework):
        digits = re.findall(r"\d+", framework)
        return 30 + int(digits[0]) if digits else 30
    return -1


while pending:
    package, version = pending.pop(0)
    match = re.search(r"\d+\.\d+(?:\.\d+)?(?:-[\w.]+)?", version)
    if not match:
        raise ValueError((package, version))
    version = match.group(0)
    if (package, version) in visited:
        continue
    visited.add((package, version))
    target = feed / f"{package}.{version}.nupkg"
    if not target.exists():
        cache = pathlib.Path.home() / ".nuget/packages" / package / version / target.name
        if cache.exists():
            shutil.copyfile(cache, target)
        else:
            url = f"https://api.nuget.org/v3-flatcontainer/{package}/{version}/{package}.{version}.nupkg"
            with urllib.request.urlopen(url, timeout=45) as response:
                data = response.read()
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                archive.testzip()
            target.write_bytes(data)
    with zipfile.ZipFile(target) as archive:
        nuspec = next(name for name in archive.namelist() if name.endswith(".nuspec"))
        tree = ET.fromstring(archive.read(nuspec))
    for element in tree.iter():
        element.tag = element.tag.rsplit("}", 1)[-1]
    dependencies = tree.find("metadata/dependencies")
    if dependencies is not None:
        groups = dependencies.findall("group")
        selected = max(groups, key=lambda group: rank(group.attrib.get("targetFramework", "")), default=dependencies)
        for dependency in selected.findall("dependency"):
            pending.append((dependency.attrib["id"].lower(), dependency.attrib["version"]))
    print(package, version, flush=True)
print(f"Feed preparado: {len(visited)} pacotes em {feed}")
