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

function unset(object, pathValue) {
  if (object == null || typeof object !== 'object') {
    return false
  }

  const path = toPath(pathValue)
  if (!isSafePath(path)) {
    return false
  }

  let current = object
  for (let index = 0; index < path.length - 1; index += 1) {
    current = current[path[index]]
    if (current == null || typeof current !== 'object') {
      return true
    }
  }

  return delete current[path[path.length - 1]]
}

module.exports = unset
