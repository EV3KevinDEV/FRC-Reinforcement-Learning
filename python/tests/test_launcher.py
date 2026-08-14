from pathlib import Path
from unittest.mock import Mock

from mosim_rl import launcher


def test_windows_worker_uses_windows_process_group(monkeypatch, tmp_path: Path) -> None:
    executable = tmp_path / "MoSimRL.exe"
    executable.touch()
    popen = Mock()
    process = Mock()
    popen.return_value = process
    monkeypatch.setattr(launcher, "_is_windows", lambda: True)
    monkeypatch.setattr(launcher.subprocess, "Popen", popen)

    worker = launcher.UnityWorkerProcess(executable, 0, 9000, tmp_path, 1)
    worker.start()

    kwargs = popen.call_args.kwargs
    assert kwargs["creationflags"] == launcher.WINDOWS_CREATE_NEW_PROCESS_GROUP
    assert "start_new_session" not in kwargs


def test_windows_worker_uses_process_methods_to_stop(monkeypatch, tmp_path: Path) -> None:
    process = Mock()
    process.poll.return_value = None
    monkeypatch.setattr(launcher, "_is_windows", lambda: True)

    worker = launcher.UnityWorkerProcess(tmp_path / "MoSimRL.exe", 0, 9000, tmp_path, 1)
    worker.process = process
    worker.stop()

    process.terminate.assert_called_once_with()
    process.wait.assert_called_once_with(timeout=10.0)
