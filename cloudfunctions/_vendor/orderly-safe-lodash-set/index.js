'use strict'

const UNSAFE_KEYS = new Set(['__proto__', 'constructor', 'prototype'])

function toPath(value) {
  if (Array.isArray(value)) {
    return value.map((segment) => String(segment))
  }

  if (value == null) {
    return []
  }

  const path = []
  String(value).replace(/[^.[\]]+|\[(?:(-?\d+)|["']([^"']+)["'])\]/g, (_, index, quoted) => {
    path.push(index !== undefined ? index : (quoted !== undefined ? quoted : _))
  })
  return path
}

function isSafePath(path) {
  return path.length > 0 && path.every((segment) => !UNSAFE_KEYS.has(segment))
}

function isIndex(segment) {
  return /^(0|[1-9]\d*)$/.test(segment)
}

function set(object, pathValue, value) {
  if (object == null || typeof object !== 'object') {
    return object
  }

  const path = toPath(pathValue)
  if (!isSafePath(path)) {
    return object
  }

  let current = object
  for (let index = 0; index < path.length - 1; index += 1) {
    const segment = path[index]
    const nextSegment = path[index + 1]
    const nextValue = current[segment]

    if (nextValue == null || typeof nextValue !== 'object') {
      current[segment] = isIndex(nextSegment) ? [] : {}
    }

    current = current[segment]
  }

  current[path[path.length - 1]] = value
  return object
}

module.exports = set
