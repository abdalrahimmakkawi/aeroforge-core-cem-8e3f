"""OpenAI-compatible LLM client pointed at NVIDIA Build / NIM.

Reads `NVIDIA_API_KEY` from the environment (and from a local `.env` file).
Completions and streaming use the standard OpenAI Python SDK against:

    https://integrate.api.nvidia.com/v1
"""

from __future__ import annotations

from lazyimp import lazy

os = (lazy)("os")
Path = (lazy)("pathlib", "Path")
load_dotenv = (lazy)("dotenv", "load_dotenv")
OpenAI = (lazy)("openai", "OpenAI")

NVIDIA_BASE_URL = "https://integrate.api.nvidia.com/v1"
# Preferred on NVIDIA Build. meta/llama-3.3-70b-instruct reached EOL (2026-08-26).
# Enable the model for your key at https://build.nvidia.com/ — many keys 404 until then.
PREFERRED_MODEL = "nvidia/nemotron-4-340b-instruct"
ALTERNATE_MODEL = "nvidia/llama-3.1-nemotron-70b-instruct"


def get_default_model() -> str:
    """Resolve the chat model: NVIDIA_MODEL env, else the preferred NIM model."""
    _load_env()
    return (os.environ.get("NVIDIA_MODEL") or PREFERRED_MODEL).strip()


# Resolved at call time via get_default_model(); kept for importers that expect a name.
DEFAULT_MODEL = PREFERRED_MODEL

_ROOT = Path(__file__).resolve().parent
_DOTENV_LOADED = False


def _load_env() -> None:
    global _DOTENV_LOADED
    if _DOTENV_LOADED:
        return
    # Prefer the project .env; do not override a key already set in the shell.
    load_dotenv(_ROOT / ".env", override=False)
    _DOTENV_LOADED = True


def get_nvidia_api_key() -> str:
    """Return the NVIDIA API key or raise if it is missing."""
    _load_env()
    key = (os.environ.get("NVIDIA_API_KEY") or "").strip()
    if not key:
        raise RuntimeError(
            "NVIDIA_API_KEY is not set. Add it to .env (see .env.example) "
            "or export it in the shell."
        )
    return key


def get_llm_client(*, api_key: str | None = None, base_url: str | None = None) -> OpenAI:
    """Build an OpenAI SDK client aimed at the NVIDIA NIM OpenAI-compatible API."""
    return OpenAI(
        api_key=api_key or get_nvidia_api_key(),
        base_url=base_url or NVIDIA_BASE_URL,
    )


def chat_complete(
    messages: list[dict[str, str]],
    *,
    model: str | None = None,
    temperature: float = 0.2,
    max_tokens: int = 1024,
    client: OpenAI | None = None,
    **kwargs,
):
    """Non-streaming chat completion via NVIDIA NIM."""
    llm = client or get_llm_client()
    return llm.chat.completions.create(
        model=model or get_default_model(),
        messages=messages,
        temperature=temperature,
        max_tokens=max_tokens,
        stream=False,
        **kwargs,
    )


def chat_stream(
    messages: list[dict[str, str]],
    *,
    model: str | None = None,
    temperature: float = 0.2,
    max_tokens: int = 1024,
    client: OpenAI | None = None,
    **kwargs,
):
    """Streaming chat completion via NVIDIA NIM (yields OpenAI stream chunks)."""
    llm = client or get_llm_client()
    return llm.chat.completions.create(
        model=model or get_default_model(),
        messages=messages,
        temperature=temperature,
        max_tokens=max_tokens,
        stream=True,
        **kwargs,
    )


def chat_text(
    messages: list[dict[str, str]],
    *,
    model: str | None = None,
    **kwargs,
) -> str:
    """Convenience wrapper that returns the assistant message text."""
    response = chat_complete(messages, model=model, **kwargs)
    choice = response.choices[0].message
    return (choice.content or "").strip()
