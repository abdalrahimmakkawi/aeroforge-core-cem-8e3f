"""NVIDIA NIM OpenAI-compatible client configuration."""

from __future__ import annotations

import os

import pytest

import llm_client


def test_default_endpoint_and_model():
    assert llm_client.NVIDIA_BASE_URL == "https://integrate.api.nvidia.com/v1"
    assert llm_client.PREFERRED_MODEL == "nvidia/nemotron-4-340b-instruct"
    assert llm_client.ALTERNATE_MODEL == "nvidia/llama-3.1-nemotron-70b-instruct"


def test_client_uses_nvidia_base_url(monkeypatch):
    monkeypatch.setenv("NVIDIA_API_KEY", "nvapi-test-key")
    monkeypatch.delenv("NVIDIA_MODEL", raising=False)
    llm_client._DOTENV_LOADED = True
    client = llm_client.get_llm_client()
    assert str(client.base_url).rstrip("/") == "https://integrate.api.nvidia.com/v1"
    assert client.api_key == "nvapi-test-key"
    assert llm_client.get_default_model() == llm_client.PREFERRED_MODEL


def test_model_env_override(monkeypatch):
    monkeypatch.setenv("NVIDIA_API_KEY", "nvapi-test-key")
    monkeypatch.setenv("NVIDIA_MODEL", "google/gemma-4-31b-it")
    llm_client._DOTENV_LOADED = True
    assert llm_client.get_default_model() == "google/gemma-4-31b-it"


def test_missing_key_raises(monkeypatch):
    monkeypatch.delenv("NVIDIA_API_KEY", raising=False)
    llm_client._DOTENV_LOADED = True
    with pytest.raises(RuntimeError, match="NVIDIA_API_KEY"):
        llm_client.get_nvidia_api_key()


@pytest.mark.integration
def test_live_completion_and_stream():
    """Hits NVIDIA only when NVIDIA_API_KEY is present; skip otherwise."""
    llm_client._DOTENV_LOADED = False
    try:
        llm_client.get_nvidia_api_key()
    except RuntimeError:
        pytest.skip("NVIDIA_API_KEY not configured")

    messages = [{"role": "user", "content": "Reply with the single word: pong"}]
    text = llm_client.chat_text(messages, max_tokens=16, temperature=0.0)
    assert "pong" in text.lower()

    chunks = []
    stream = llm_client.chat_stream(messages, max_tokens=16, temperature=0.0)
    for chunk in stream:
        delta = chunk.choices[0].delta.content if chunk.choices else None
        if delta:
            chunks.append(delta)
    assert "".join(chunks).strip()
