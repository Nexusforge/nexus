import os
import re
from pathlib import Path

import setuptools

source_dir = os.getcwd()

build_dir = "../../../artifacts/obj/python-client"
Path(build_dir).mkdir(parents=True, exist_ok=True)
os.chdir(build_dir)

with open(os.path.join(source_dir, "README.md"), "r") as fh:
    long_description = fh.read()

with open("../../../src/Directory.Build.props", "r") as fh:

    content = fh.read()

    match = re.match(".*" + \
        "<Authors>(?P<authors>.*)<\\/Authors>.*" + \
        "<Major>(?P<major>.*)<\\/Major>.*" + \
        "<Minor>(?P<minor>.*)<\\/Minor>.*" + \
        "<Revision>(?P<revision>.*)<\\/Revision>.*" + \
        "<VersionSuffix>(?P<version_suffix>.*)<\\/VersionSuffix>.*" + \
        "<PackageLicenseExpression>(?P<package_license_expression>.*)<\\/PackageLicenseExpression>.*" + \
        "<PackageProjectUrl>(?P<package_project_url>.*)<\\/PackageProjectUrl>.*" + \
        "<RepositoryUrl>(?P<repository_url>.*)<\\/RepositoryUrl>.*", \
        content, re.DOTALL)

    assert match

    # author
    author = match.group("authors")

    # version
    major = match.group("major")
    minor = match.group("minor")
    revision = match.group("revision")
    version_suffix = match.group("version_suffix")
    build = os.getenv("APPVEYOR_BUILD_NUMBER")
    isFinalBuild = os.getenv("APPVEYOR_REPO_TAG") == "true"

    if isFinalBuild:
        # "final": PEP440 does not support SemVer versioning (https://semver.org/#spec-item-9)
        build = None

    version = f"{major}.{minor}.{revision}"

    if version_suffix:
        version = f"{version}-{version_suffix}"

        if build:
            # PEP440 does not support SemVer versioning (https://semver.org/#spec-item-9)
            version = f"{version}{int(build):03d}"

    # others
    license = match.group("package_license_expression")
    project_url = match.group("package_project_url")
    repository_url = match.group("repository_url")

# setuptools normalizes SemVer version :-/ https://github.com/pypa/setuptools/issues/308
# The solution suggested there (from setuptools import sic, then call sic(version))
# is useless here because setuptools calls packaging.version.Version when .egg is created
# which again normalizes the version.

setuptools.setup(
    name="nexusapi",
    version=version,
    description="Client for the Nexus system.",
    long_description=long_description,
    long_description_content_type="text/markdown",
    author=author,
    url="https://github.com/Nexusforge/nexus",
    packages=[
        "nexusapi"
    ],
    project_urls={
        "Project": project_url,
        "Repository": repository_url,
    },
    classifiers=[
        "Programming Language :: Python :: 3",
        "License :: OSI Approved :: MIT License",
        "Operating System :: OS Independent"
    ],
    license=license,
    keywords="Nexus time-series data lake",
    platforms=[
        "any"
    ],
    package_dir={
        "nexusapi": os.path.join(source_dir, "nexusapi")
    },
    python_requires=">=3.9",
    install_requires=[
        "httpx>=0.22.0"
    ]
)
