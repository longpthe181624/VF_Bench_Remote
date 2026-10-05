import { Component } from 'react'
import { Link } from 'react-router-dom'
import { Button, ErrorMessage } from './ui'

export class PageErrorBoundary extends Component {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  componentDidUpdate(previous) {
    if (this.state.failed && previous.resetKey !== this.props.resetKey) {
      this.setState({ failed: false })
    }
  }

  render() {
    if (!this.state.failed) return this.props.children
    return (
      <section className="panel">
        <ErrorMessage>Không thể hiển thị trang này. Bạn có thể thử lại hoặc mở trang khác từ menu.</ErrorMessage>
        <div className="flex gap-2 mt-5">
          <Button onClick={() => this.setState({ failed: false })}>Thử lại</Button>
          <Link className="btn" to="/devices">Về màn thiết bị</Link>
        </div>
      </section>
    )
  }
}
