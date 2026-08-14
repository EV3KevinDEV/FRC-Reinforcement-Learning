from pathlib import Path

from mosim_rl import cli


def test_default_windows_executables(monkeypatch) -> None:
    monkeypatch.delenv("MOSIM_EXECUTABLE", raising=False)
    monkeypatch.setattr(cli, "_is_windows", lambda: True)

    assert cli.default_executable() == (
        Path(__file__).resolve().parents[2]
        / "_Build"
        / "RL"
        / "WindowsServer"
        / "MoSimRL.exe"
    )
    assert cli.development_executable() == (
        Path(__file__).resolve().parents[2]
        / "_Build"
        / "RL"
        / "WindowsDevelopment"
        / "MoSimRL.exe"
    )


def test_default_linux_executables(monkeypatch) -> None:
    monkeypatch.delenv("MOSIM_EXECUTABLE", raising=False)
    monkeypatch.setattr(cli, "_is_windows", lambda: False)

    assert cli.default_executable().name == "MoSimRL.x86_64"
    assert cli.default_executable().parent.name == "LinuxServer"
    assert cli.development_executable().parent.name == "LinuxDevelopment"
