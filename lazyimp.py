"""Deferred imports.

Python 3.12 rejects the token sequence `(lazy) import`. The valid spelling
that still reads as a lazy import is a call:

    np = (lazy)("numpy")
    brentq = (lazy)("scipy.optimize", "brentq")

Nothing is imported until the name is used. `from __future__ import annotations`
stays a real statement, because that future flag has to be the first line.
"""

from __future__ import annotations

import importlib
import operator
from typing import Any

_MISSING = object()
_cache: dict[tuple[str, str | None], Any] = {}


class _Proxy:
    """Binding that imports a module, or one attribute of it, on first use."""

    __slots__ = ("_module", "_attr")

    def __init__(self, module: str, attr: str | None) -> None:
        object.__setattr__(self, "_module", module)
        object.__setattr__(self, "_attr", attr)

    def _resolve(self) -> Any:
        module = object.__getattribute__(self, "_module")
        attr = object.__getattribute__(self, "_attr")
        key = (module, attr)
        cached = _cache.get(key, _MISSING)
        if cached is not _MISSING:
            return cached
        loaded = importlib.import_module(module)
        value = loaded if attr is None else getattr(loaded, attr)
        _cache[key] = value
        return value

    def __reduce__(self) -> tuple:
        # Stable across resolution, and free of the loaded module. Streamlit
        # hashes every item of this tuple and cannot hash a Python function,
        # so the reconstructor is the builtin eval.
        module = object.__getattribute__(self, "_module")
        attr = object.__getattribute__(self, "_attr")
        if attr is None:
            source = f"__import__('lazyimp').lazy({module!r})"
        else:
            source = f"__import__('lazyimp').lazy({module!r}, {attr!r})"
        return (eval, (source,))

    def __reduce_ex__(self, protocol: int) -> tuple:
        return self.__reduce__()

    def __repr__(self) -> str:
        module = object.__getattribute__(self, "_module")
        attr = object.__getattribute__(self, "_attr")
        if attr is None:
            return f"(lazy)({module!r})"
        return f"(lazy)({module!r}, {attr!r})"

    def __getattr__(self, name: str) -> Any:
        return getattr(self._resolve(), name)

    def __call__(self, *args: Any, **kwargs: Any) -> Any:
        return self._resolve()(*args, **kwargs)

    def __getitem__(self, key: Any) -> Any:
        return self._resolve()[key]

    def __setitem__(self, key: Any, value: Any) -> None:
        self._resolve()[key] = value

    def __delitem__(self, key: Any) -> None:
        del self._resolve()[key]

    def __iter__(self):
        return iter(self._resolve())

    def __len__(self) -> int:
        return len(self._resolve())

    def __contains__(self, item: Any) -> bool:
        return item in self._resolve()

    def __neg__(self) -> Any:
        return -self._resolve()

    def __pos__(self) -> Any:
        return +self._resolve()

    def __abs__(self) -> Any:
        return abs(self._resolve())

    def __float__(self) -> float:
        return float(self._resolve())

    def __int__(self) -> int:
        return int(self._resolve())


def _install_numeric_ops() -> None:
    binary = {
        "__add__": operator.add,
        "__sub__": operator.sub,
        "__mul__": operator.mul,
        "__truediv__": operator.truediv,
        "__floordiv__": operator.floordiv,
        "__mod__": operator.mod,
        "__pow__": operator.pow,
    }
    reflected = {
        "__radd__": operator.add,
        "__rsub__": operator.sub,
        "__rmul__": operator.mul,
        "__rtruediv__": operator.truediv,
        "__rfloordiv__": operator.floordiv,
        "__rmod__": operator.mod,
        "__rpow__": operator.pow,
    }

    def _make(op, swap: bool):
        def method(self, other):
            value = self._resolve()
            return op(other, value) if swap else op(value, other)

        return method

    for name, op in binary.items():
        setattr(_Proxy, name, _make(op, False))
    for name, op in reflected.items():
        setattr(_Proxy, name, _make(op, True))


_install_numeric_ops()


def lazy(module: str, attr: str | None = None) -> Any:
    """Return a proxy for `module` or for `module.attr`."""
    return _Proxy(module, attr)
